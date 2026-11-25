using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace FarPlayerMarkers
{
    /// <summary>
    /// Draws, on the world map and minimap:
    /// - every online player at any distance (a dot with a point showing which way they face),
    ///   kept on the map's edge when they're outside the area shown;
    /// - waypoints other players have placed, with their chosen icon and colour.
    /// Hovering over a marker shows the name and how far away it is.
    /// </summary>
    public class FarPlayersMapLayer : MapLayer
    {
        /// <summary>Facing directions are rounded to this many steps, so textures can be reused.</summary>
        private const int DirectionSteps = 32;

        private readonly ICoreClientAPI capi;
        private readonly Dictionary<string, LoadedTexture> textureCache = new Dictionary<string, LoadedTexture>();
        private FarPlayerMarkersMod modSystem;
        private Vec2f viewPos = new Vec2f();

        public FarPlayersMapLayer(ICoreAPI api, IWorldMapManager mapSink) : base(api, mapSink)
        {
            capi = api as ICoreClientAPI;
        }

        public override string Title => Lang.Get("farplayermarkers:layer-title");

        public override string LayerGroupCode => "farplayermarkers";

        public override EnumMapAppSide DataSide => EnumMapAppSide.Client;

        private FarPlayerMarkersMod Mod
        {
            get
            {
                if (modSystem == null && capi != null)
                {
                    modSystem = capi.ModLoader.GetModSystem<FarPlayerMarkersMod>();
                    if (modSystem != null) modSystem.ConfigChanged += ClearTextures;
                }
                return modSystem;
            }
        }

        public override void Render(GuiElementMap mapElem, float dt)
        {
            if (!Active || capi == null) return;

            FarPlayerMarkersMod mod = Mod;
            if (mod == null || mod.ClientConfig == null) return;

            ClientConfig cfg = mod.ClientConfig;
            float size = (float)GuiElement.scaled(cfg.MarkerSize);
            string myUid = capi.World.Player != null ? capi.World.Player.PlayerUID : null;
            long now = capi.World.ElapsedMilliseconds;

            // Other players' waypoints (drawn first so players appear on top)
            if (cfg.ShowOthersWaypoints && mod.SharedWaypoints != null)
            {
                float wpSize = (float)GuiElement.scaled(cfg.WaypointSize);
                foreach (SharedWaypoint wp in mod.SharedWaypoints)
                {
                    if (wp == null || wp.OwnerUid == myUid) continue;
                    DrawWaypoint(mapElem, wp, wpSize, mod, cfg);
                }
            }

            // Other players
            foreach (KnownPlayer kp in mod.KnownPlayers.Values)
            {
                if (kp.Uid == myUid) continue;
                if (now - kp.ReceivedAtMs > mod.StaleAfterMs) continue;

                Vec3d pos;
                float yaw;
                GetPose(kp, out pos, out yaw);

                string fill = cfg.OtherFillColorHex;
                string border = cfg.OtherBorderColorHex;
                if (cfg.UsePlayerSelectedColors)
                {
                    if (ColorHex.Normalize(kp.FillHex) != null) fill = kp.FillHex;
                    if (ColorHex.Normalize(kp.BorderHex) != null) border = kp.BorderHex;
                }

                DrawMarker(mapElem, pos, yaw, fill, border, size, cfg.KeepPlayersAtMapEdge);
            }

            // Yourself
            if (capi.World.Player != null && capi.World.Player.Entity != null)
            {
                var me = capi.World.Player.Entity;
                DrawMarker(mapElem, me.Pos.XYZ, me.Pos.Yaw, cfg.OwnFillColorHex, cfg.OwnBorderColorHex, size, false);
            }
        }

        /// <summary>Faded colour for markers held on the map edge.</summary>
        private static readonly Vec4f EdgeTint = new Vec4f(1, 1, 1, 0.6f);

        private void DrawMarker(GuiElementMap mapElem, Vec3d worldPos, float yaw, string fillHex, string borderHex, float size, bool keepAtEdge)
        {
            double x, y;
            bool clamped;
            if (!ScreenPos(mapElem, worldPos, size, keepAtEdge, out x, out y, out clamped)) return;

            LoadedTexture tex = GetTexture(fillHex, borderHex, DirectionIndex(yaw), size);
            if (tex == null) return;

            capi.Render.Render2DTexturePremultipliedAlpha(
                tex.TextureId,
                (float)(x - size / 2),
                (float)(y - size / 2),
                size,
                size,
                60,
                clamped ? EdgeTint : null
            );
        }

        private void DrawWaypoint(GuiElementMap mapElem, SharedWaypoint wp, float size, FarPlayerMarkersMod mod, ClientConfig cfg)
        {
            double x, y;
            bool clamped;
            if (!ScreenPos(mapElem, new Vec3d(wp.X, wp.Y, wp.Z), size, wp.Pinned, out x, out y, out clamped)) return;

            // Normally the waypoint's own colour with a black outline. A death marker is drawn in
            // the colours of the player who died (the same colours as their player marker).
            int fillArgb = OpaqueColor(wp.Color);
            int borderArgb = OpaqueColor(0);
            string iconName = wp.Icon;
            if (IsDeathWaypoint(wp))
            {
                string fillHex, borderHex;
                OwnerColors(wp, mod, cfg, out fillHex, out borderHex);
                fillArgb = HexToArgb(fillHex, fillArgb);
                borderArgb = HexToArgb(borderHex, borderArgb);
                if (string.IsNullOrEmpty(iconName)) iconName = "gravestone";
            }

            LoadedTexture icon = GetIconTexture(iconName, fillArgb);
            LoadedTexture outline = icon != null ? GetIconTexture(iconName, borderArgb) : null;

            if (icon != null)
            {
                if (outline != null)
                {
                    float o = size * 1.2f;
                    capi.Render.Render2DTexturePremultipliedAlpha(outline.TextureId,
                        (float)(x - o / 2), (float)(y - o / 2), o, o, 54);
                }
                capi.Render.Render2DTexturePremultipliedAlpha(icon.TextureId,
                    (float)(x - size / 2), (float)(y - size / 2), size, size, 55);
                return;
            }

            // Icon file couldn't be found: fall back to a diamond in the waypoint's colour.
            LoadedTexture tex = GetWaypointTexture(fillArgb & 0x00FFFFFF, size);
            if (tex == null) return;

            capi.Render.Render2DTexturePremultipliedAlpha(tex.TextureId,
                (float)(x - size / 2), (float)(y - size / 2), size, size, 55);
        }

        /// <summary>
        /// The colours a player's marker is drawn in: what they chose (live if they're online, else
        /// what the server remembered), or your "others" colours if player-chosen colours are off.
        /// </summary>
        private static void OwnerColors(SharedWaypoint wp, FarPlayerMarkersMod mod, ClientConfig cfg, out string fillHex, out string borderHex)
        {
            fillHex = cfg.OtherFillColorHex;
            borderHex = cfg.OtherBorderColorHex;
            if (!cfg.UsePlayerSelectedColors) return;

            string fill = wp.OwnerFillHex, border = wp.OwnerBorderHex;
            KnownPlayer kp;
            if (wp.OwnerUid != null && mod.KnownPlayers.TryGetValue(wp.OwnerUid, out kp))
            {
                if (ColorHex.Normalize(kp.FillHex) != null) fill = kp.FillHex;
                if (ColorHex.Normalize(kp.BorderHex) != null) border = kp.BorderHex;
            }
            if (ColorHex.Normalize(fill) != null) fillHex = fill;
            if (ColorHex.Normalize(border) != null) borderHex = border;
        }

        /// <summary>A "#RRGGBB" colour as an opaque ARGB int, or the fallback if it can't be read.</summary>
        private static int HexToArgb(string hex, int fallback)
        {
            double r, g, b, a;
            if (!ColorHex.TryParse(hex, out r, out g, out b, out a)) return fallback;
            int ri = (int)Math.Round(r * 255), gi = (int)Math.Round(g * 255), bi = (int)Math.Round(b * 255);
            return unchecked((int)(0xFF000000u | ((uint)ri << 16) | ((uint)gi << 8) | (uint)bi));
        }

        /// <summary>Waypoint colours are stored as RGB; make sure they are fully opaque (ARGB).</summary>
        private static int OpaqueColor(int color)
        {
            return unchecked((int)(0xFF000000 | ((uint)color & 0x00FFFFFF)));
        }

        // ---------------- Waypoint icons ----------------

        /// <summary>Icon name (e.g. "pick") to its .svg file, built by scanning the game's icon folder.</summary>
        private Dictionary<string, AssetLocation> iconFiles;

        /// <summary>Icons that failed to load, so we don't retry every frame.</summary>
        private readonly Dictionary<string, bool> missingIcons = new Dictionary<string, bool>();

        /// <summary>
        /// Finds every waypoint icon the game knows (textures/icons/worldmap/*.svg, any mod).
        /// File names can carry a sort prefix such as "1-circle.svg", so both "1-circle" and
        /// "circle" are recorded. The asset list is read by reflection because source mods can't
        /// use the game's List&lt;T&gt; return types directly (error CS0012).
        /// </summary>
        private void BuildIconIndex()
        {
            iconFiles = new Dictionary<string, AssetLocation>();
            try
            {
                object assets = capi.Assets;
                System.Reflection.MethodInfo getMany = null;
                foreach (System.Reflection.MethodInfo m in assets.GetType().GetMethods())
                {
                    if (m.Name != "GetMany") continue;
                    System.Reflection.ParameterInfo[] ps = m.GetParameters();
                    if (ps.Length == 3 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType == typeof(string) && ps[2].ParameterType == typeof(bool))
                    {
                        getMany = m;
                        break;
                    }
                }
                if (getMany == null) return;

                System.Collections.IEnumerable found = getMany.Invoke(assets, new object[] { "textures/icons/worldmap/", null, false }) as System.Collections.IEnumerable;
                if (found == null) return;

                foreach (object item in found)
                {
                    IAsset asset = item as IAsset;
                    if (asset == null || asset.Location == null) continue;

                    string path = asset.Location.Path;
                    if (path == null || !path.EndsWith(".svg")) continue;

                    string file = path.Substring(path.LastIndexOf('/') + 1);
                    string name = file.Substring(0, file.Length - 4);

                    AddIconName(name, asset.Location);
                    AddIconName(asset.Location.Domain + ":" + name, asset.Location);

                    // Strip a numeric sort prefix like "12-"
                    int dash = name.IndexOf('-');
                    if (dash > 0 && IsDigits(name.Substring(0, dash)))
                    {
                        string bare = name.Substring(dash + 1);
                        AddIconName(bare, asset.Location);
                        AddIconName(asset.Location.Domain + ":" + bare, asset.Location);
                    }
                }

                capi.Logger.Notification("[farplayermarkers] Found {0} waypoint icon names", iconFiles.Count);
            }
            catch (Exception e)
            {
                capi.Logger.Warning("[farplayermarkers] Could not list waypoint icons: {0}", e.Message);
            }
        }

        private void AddIconName(string name, AssetLocation loc)
        {
            if (!iconFiles.ContainsKey(name)) iconFiles[name] = loc;
        }

        private static bool IsDigits(string s)
        {
            if (s.Length == 0) return false;
            foreach (char c in s) if (c < '0' || c > '9') return false;
            return true;
        }

        private AssetLocation FindIconFile(string icon)
        {
            if (iconFiles == null) BuildIconIndex();

            AssetLocation loc;
            if (iconFiles != null && iconFiles.TryGetValue(icon, out loc)) return loc;

            // Not in the index: try the plain path as a last resort.
            string domain = "game";
            string name = icon;
            int colon = icon.IndexOf(':');
            if (colon > 0)
            {
                domain = icon.Substring(0, colon);
                name = icon.Substring(colon + 1);
            }
            AssetLocation guess = new AssetLocation(domain, "textures/icons/worldmap/" + name + ".svg");
            return capi.Assets.TryGet(guess) != null ? guess : null;
        }

        /// <summary>
        /// A waypoint icon drawn in one exact colour (the colour is baked into the image, so every
        /// colour shows correctly). Cached per icon and colour.
        /// </summary>
        private LoadedTexture GetIconTexture(string icon, int argb)
        {
            if (string.IsNullOrEmpty(icon)) icon = "circle";

            // Fixed, fairly large texture size; it's scaled down when drawn, so it stays sharp.
            int px = Math.Max(24, (int)GuiElement.scaled(32));
            string key = "icon|" + icon + "|" + argb + "|" + px;

            LoadedTexture existing;
            if (textureCache.TryGetValue(key, out existing)) return existing;

            string missingKey = "icon|" + icon;
            if (missingIcons.ContainsKey(missingKey)) return null;

            try
            {
                AssetLocation loc = FindIconFile(icon);
                if (loc == null)
                {
                    missingIcons[missingKey] = true;
                    capi.Logger.Notification("[farplayermarkers] No icon file found for waypoint icon '{0}', using a diamond", icon);
                    return null;
                }

                LoadedTexture tex = capi.Gui.LoadSvgWithPadding(loc, px, px, 2, argb);
                textureCache[key] = tex;
                return tex;
            }
            catch (Exception e)
            {
                capi.Logger.Warning("[farplayermarkers] Could not load waypoint icon '{0}': {1}", icon, e.Message);
                missingIcons[missingKey] = true;
                return null;
            }
        }

        /// <summary>
        /// Works out where a world position lands on the map element.
        /// Returns false if it's off the map, unless keepAtEdge is set, in which case it's moved
        /// onto the nearest edge (clamped = true).
        /// </summary>
        private bool ScreenPos(GuiElementMap mapElem, Vec3d worldPos, float size, bool keepAtEdge, out double x, out double y, out bool clamped)
        {
            mapElem.TranslateWorldPosToViewPos(worldPos, ref viewPos);

            double left = mapElem.Bounds.renderX;
            double top = mapElem.Bounds.renderY;
            double right = left + mapElem.Bounds.InnerWidth;
            double bottom = top + mapElem.Bounds.InnerHeight;

            x = left + viewPos.X;
            y = top + viewPos.Y;
            clamped = false;

            bool inside = x >= left && x <= right && y >= top && y <= bottom;
            if (inside) return true;
            if (!keepAtEdge) return false;

            double half = size / 2;
            double cx = (left + right) / 2;
            double cy = (top + bottom) / 2;
            double halfW = (right - left) / 2 - half;
            double halfH = (bottom - top) / 2 - half;
            if (halfW <= 0 || halfH <= 0) return false;

            // Slide the point along the line from the map centre until it meets the edge,
            // so the marker sits in the direction of the player.
            double dx = x - cx;
            double dy = y - cy;
            double scale = Math.Min(
                dx != 0 ? halfW / Math.Abs(dx) : double.MaxValue,
                dy != 0 ? halfH / Math.Abs(dy) : double.MaxValue);

            x = cx + dx * scale;
            y = cy + dy * scale;
            clamped = true;
            return true;
        }

        public override void OnMouseMoveClient(MouseEvent args, GuiElementMap mapElem, StringBuilder hoverText)
        {
            if (!Active || capi == null) return;

            FarPlayerMarkersMod mod = Mod;
            if (mod == null || mod.ClientConfig == null) return;

            string myUid = capi.World.Player != null ? capi.World.Player.PlayerUID : null;
            long now = capi.World.ElapsedMilliseconds;
            float size = (float)GuiElement.scaled(mod.ClientConfig.MarkerSize);
            double half = size / 2;

            Vec3d myPos = null;
            if (capi.World.Player != null && capi.World.Player.Entity != null) myPos = capi.World.Player.Entity.Pos.XYZ;

            foreach (KnownPlayer kp in mod.KnownPlayers.Values)
            {
                if (kp.Uid == myUid) continue;
                if (now - kp.ReceivedAtMs > mod.StaleAfterMs) continue;

                Vec3d pos;
                float yaw;
                GetPose(kp, out pos, out yaw);

                double x, y;
                bool clamped;
                if (!ScreenPos(mapElem, pos, size, mod.ClientConfig.KeepPlayersAtMapEdge, out x, out y, out clamped)) continue;
                if (Math.Abs(x - args.X) > half || Math.Abs(y - args.Y) > half) continue;

                hoverText.AppendLine(myPos != null
                    ? kp.Name + " (" + Lang.Get("farplayermarkers:blocks-away", (int)Math.Round(myPos.DistanceTo(pos))) + ")"
                    : kp.Name);
            }

            if (!mod.ClientConfig.ShowOthersWaypoints || mod.SharedWaypoints == null) return;

            float wpSize = (float)GuiElement.scaled(mod.ClientConfig.WaypointSize);
            double wpHalf = wpSize / 2;
            foreach (SharedWaypoint wp in mod.SharedWaypoints)
            {
                if (wp == null || wp.OwnerUid == myUid) continue;

                Vec3d pos = new Vec3d(wp.X, wp.Y, wp.Z);
                double x, y;
                bool clamped;
                if (!ScreenPos(mapElem, pos, wpSize, wp.Pinned, out x, out y, out clamped)) continue;
                if (Math.Abs(x - args.X) > wpHalf || Math.Abs(y - args.Y) > wpHalf) continue;

                hoverText.AppendLine(HoverLine(wp, myPos != null ? (int?)(int)Math.Round(myPos.DistanceTo(pos)) : null));
            }
        }

        /// <summary>
        /// Hover text for a shared waypoint, worded for the person looking at it.
        /// The game names a death waypoint "You died here" from its owner's point of view,
        /// so for everyone else it becomes "Name died here".
        /// </summary>
        private static string HoverLine(SharedWaypoint wp, int? distance)
        {
            string owner = string.IsNullOrEmpty(wp.OwnerName) ? Lang.Get("farplayermarkers:unknown") : wp.OwnerName;
            string dist = distance.HasValue ? Lang.Get("farplayermarkers:blocks-away", distance.Value) : null;

            if (IsDeathWaypoint(wp))
            {
                string died = Lang.Get("farplayermarkers:died-here", owner);
                return dist != null ? died + " (" + dist + ")" : died;
            }

            string title = string.IsNullOrWhiteSpace(wp.Title) ? Lang.Get("farplayermarkers:waypoint") : wp.Title.Trim();
            string whose = Lang.Get("farplayermarkers:owners-waypoint", owner);
            return title + " (" + whose + (dist != null ? ", " + dist : "") + ")";
        }

        /// <summary>
        /// The game's automatic death waypoint: titled "You died here" (in the server's language).
        /// A waypoint a player named themselves is left alone, even with the gravestone icon.
        /// </summary>
        private static bool IsDeathWaypoint(SharedWaypoint wp)
        {
            if (string.IsNullOrWhiteSpace(wp.Title)) return false;
            string t = wp.Title.Trim();
            if (string.Equals(t, "You died here", StringComparison.OrdinalIgnoreCase)) return true;
            string local = Lang.Get("You died here");
            return !string.IsNullOrEmpty(local) && string.Equals(t, local.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Uses the live position when the player is close enough to be loaded,
        /// otherwise the position the server last sent.
        /// </summary>
        private void GetPose(KnownPlayer kp, out Vec3d pos, out float yaw)
        {
            IPlayer player = capi.World.PlayerByUid(kp.Uid);
            if (player != null && player.Entity != null)
            {
                pos = player.Entity.Pos.XYZ;
                yaw = player.Entity.Pos.Yaw;
                return;
            }

            pos = new Vec3d(kp.X, kp.Y, kp.Z);
            yaw = kp.Yaw;
        }

        /// <summary>
        /// Turns a yaw into one of DirectionSteps screen angles.
        /// Same rotation the game uses for its own player arrow: -yaw + 180 degrees.
        /// </summary>
        private static int DirectionIndex(float yaw)
        {
            double angle = -yaw + Math.PI;
            double steps = angle / (2 * Math.PI) * DirectionSteps;
            int index = (int)Math.Round(steps) % DirectionSteps;
            if (index < 0) index += DirectionSteps;
            return index;
        }

        private LoadedTexture GetTexture(string fillHex, string borderHex, int direction, float size)
        {
            int px = Math.Max(10, (int)Math.Ceiling(size));
            string key = fillHex + "|" + borderHex + "|" + direction + "|" + px;

            LoadedTexture existing;
            if (textureCache.TryGetValue(key, out existing)) return existing;

            double fr, fg, fb, fa, br, bg, bb, ba;
            if (!ColorHex.TryParse(fillHex, out fr, out fg, out fb, out fa)) { fr = 0.2; fg = 0.6; fb = 1; fa = 1; }
            if (!ColorHex.TryParse(borderHex, out br, out bg, out bb, out ba)) { br = 0; bg = 0; bb = 0; ba = 1; }

            double angle = direction * (2 * Math.PI / DirectionSteps);
            double c = px / 2.0;
            double radius = px * 0.28;
            double line = Math.Max(1.5, px / 12.0);

            LoadedTexture tex;
            using (Cairo.ImageSurface surface = new Cairo.ImageSurface(Cairo.Format.Argb32, px, px))
            using (Cairo.Context ctx = new Cairo.Context(surface))
            {
                // Rotate around the centre so "up" points the way the player faces.
                ctx.Translate(c, c);
                ctx.Rotate(angle);
                ctx.Translate(-c, -c);

                // Pointer
                ctx.MoveTo(c, line);
                ctx.LineTo(c + radius * 0.85, c - radius * 0.35);
                ctx.LineTo(c - radius * 0.85, c - radius * 0.35);
                ctx.ClosePath();
                ctx.SetSourceRGBA(fr, fg, fb, fa);
                ctx.FillPreserve();
                ctx.LineWidth = line;
                ctx.SetSourceRGBA(br, bg, bb, ba);
                ctx.Stroke();

                // Dot
                ctx.NewPath();
                ctx.Arc(c, c, radius, 0, 2 * Math.PI);
                ctx.SetSourceRGBA(fr, fg, fb, fa);
                ctx.FillPreserve();
                ctx.LineWidth = line;
                ctx.SetSourceRGBA(br, bg, bb, ba);
                ctx.Stroke();

                int textureId = capi.Gui.LoadCairoTexture(surface, true);
                tex = new LoadedTexture(capi, textureId, px, px);
            }

            textureCache[key] = tex;
            return tex;
        }

        /// <summary>A diamond in the waypoint's colour with a dark border.</summary>
        private LoadedTexture GetWaypointTexture(int color, float size)
        {
            int px = Math.Max(8, (int)Math.Ceiling(size));
            string key = "wp|" + color + "|" + px;

            LoadedTexture existing;
            if (textureCache.TryGetValue(key, out existing)) return existing;

            double r = ((color >> 16) & 0xFF) / 255.0;
            double g = ((color >> 8) & 0xFF) / 255.0;
            double b = (color & 0xFF) / 255.0;
            double c = px / 2.0;
            double line = Math.Max(1.5, px / 10.0);
            double reach = c - line;

            LoadedTexture tex;
            using (Cairo.ImageSurface surface = new Cairo.ImageSurface(Cairo.Format.Argb32, px, px))
            using (Cairo.Context ctx = new Cairo.Context(surface))
            {
                ctx.MoveTo(c, c - reach);
                ctx.LineTo(c + reach, c);
                ctx.LineTo(c, c + reach);
                ctx.LineTo(c - reach, c);
                ctx.ClosePath();
                ctx.SetSourceRGBA(r, g, b, 1);
                ctx.FillPreserve();
                ctx.LineWidth = line;
                ctx.SetSourceRGBA(0.1, 0.1, 0.1, 1);
                ctx.Stroke();

                int textureId = capi.Gui.LoadCairoTexture(surface, true);
                tex = new LoadedTexture(capi, textureId, px, px);
            }

            textureCache[key] = tex;
            return tex;
        }

        private void ClearTextures()
        {
            foreach (LoadedTexture tex in textureCache.Values)
            {
                if (tex != null) tex.Dispose();
            }
            textureCache.Clear();
        }

        public override void Dispose()
        {
            if (modSystem != null) modSystem.ConfigChanged -= ClearTextures;
            ClearTextures();
            base.Dispose();
        }
    }
}
