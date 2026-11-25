using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace FarPlayerMarkers
{
    /// <summary>A player position the client last heard about from the server.</summary>
    public class KnownPlayer
    {
        public string Uid;
        public string Name;
        public double X;
        public double Y;
        public double Z;
        public float Yaw;
        public string FillHex;
        public string BorderHex;
        public long ReceivedAtMs;
    }

    public class FarPlayerMarkersMod : ModSystem
    {
        public const string ChannelName = "farplayermarkers";
        public const string LayerCode = "farplayermarkers";
        public const string ClientConfigFile = "FarPlayerMarkersConfig.json";
        public const string ServerConfigFile = "FarPlayerMarkersServerConfig.json";

        // ---------------- Client state ----------------

        /// <summary>Latest known position of every online player, by player UID.</summary>
        public readonly Dictionary<string, KnownPlayer> KnownPlayers = new Dictionary<string, KnownPlayer>();

        public ClientConfig ClientConfig { get; private set; }

        /// <summary>Whether the server says this player may change server settings.</summary>
        public bool IsAdmin { get; private set; }

        public int ServerUpdateIntervalMs { get; private set; } = 1000;
        public bool ServerHideCrouchingPlayers { get; private set; }
        public bool ServerShareWaypoints { get; private set; } = true;

        /// <summary>Every player's waypoints, as last sent by the server.</summary>
        public SharedWaypoint[] SharedWaypoints { get; private set; } = new SharedWaypoint[0];

        /// <summary>Raised when the player's colours change, so the map can redraw its markers.</summary>
        public event Action ConfigChanged;

        private ICoreClientAPI capi;
        private IClientNetworkChannel clientChannel;
        private GuiDialogFarMarkersSettings settingsDialog;

        // ---------------- Server state ----------------

        private ICoreServerAPI sapi;
        private IServerNetworkChannel serverChannel;
        private ServerConfig serverConfig;
        private long tickListenerId;
        private string lastWaypointSignature;

        /// <summary>How often the server checks for waypoint changes, in milliseconds.</summary>
        private const int WaypointCheckIntervalMs = 3000;
        private readonly Dictionary<string, PlayerColorPacket> playerColors = new Dictionary<string, PlayerColorPacket>();

        public override bool ShouldLoad(EnumAppSide forSide)
        {
            return true;
        }

        public override void Start(ICoreAPI api)
        {
            api.Network
                .RegisterChannel(ChannelName)
                .RegisterMessageType<PlayerPositionsPacket>()
                .RegisterMessageType<PlayerColorPacket>()
                .RegisterMessageType<ServerSettingsPacket>()
                .RegisterMessageType<SaveServerSettingsPacket>()
                .RegisterMessageType<SharedWaypointsPacket>();

            WorldMapManager mapManager = api.ModLoader.GetModSystem<WorldMapManager>();
            if (mapManager != null)
            {
                mapManager.RegisterMapLayer<FarPlayersMapLayer>(LayerCode, 0.75);
            }
        }

        // =====================================================================
        // Server
        // =====================================================================

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            serverConfig = LoadServerConfig(api);

            serverChannel = api.Network.GetChannel(ChannelName);
            serverChannel.SetMessageHandler<PlayerColorPacket>(OnPlayerColorReceived);
            serverChannel.SetMessageHandler<SaveServerSettingsPacket>(OnSaveServerSettingsReceived);

            api.Event.PlayerJoin += (player) =>
            {
                IServerPlayer splayer = player as IServerPlayer;
                SendServerSettings(splayer);
                SendWaypointsTo(splayer);
            };
            api.Event.PlayerLeave += (player) =>
            {
                if (player != null) playerColors.Remove(player.PlayerUID);
            };

            StartTicking();
            api.Event.RegisterGameTickListener(OnWaypointTick, WaypointCheckIntervalMs);
            api.Logger.Notification("[farplayermarkers] Server side started, sending player positions every {0} ms", serverConfig.UpdateIntervalMs);
        }

        private ServerConfig LoadServerConfig(ICoreServerAPI api)
        {
            ServerConfig config = null;
            try
            {
                config = api.LoadModConfig<ServerConfig>(ServerConfigFile);
            }
            catch (Exception e)
            {
                api.Logger.Warning("[farplayermarkers] Could not read {0}, using defaults: {1}", ServerConfigFile, e.Message);
            }

            if (config == null) config = new ServerConfig();
            config.Clamp();
            api.StoreModConfig(config, ServerConfigFile);
            return config;
        }

        private void StartTicking()
        {
            if (tickListenerId != 0) sapi.Event.UnregisterGameTickListener(tickListenerId);
            tickListenerId = sapi.Event.RegisterGameTickListener(OnServerTick, serverConfig.UpdateIntervalMs);
        }

        private void OnServerTick(float dt)
        {
            List<IServerPlayer> playing = new List<IServerPlayer>();
            List<PlayerPositionEntry> entries = new List<PlayerPositionEntry>();

            foreach (IPlayer player in sapi.World.AllOnlinePlayers)
            {
                IServerPlayer splayer = player as IServerPlayer;
                if (splayer == null) continue;
                if (splayer.ConnectionState != EnumClientState.Playing) continue;
                if (splayer.Entity == null) continue;

                playing.Add(splayer);

                if (serverConfig.HideCrouchingPlayers && splayer.Entity.Controls.Sneak) continue;

                PlayerColorPacket colors;
                playerColors.TryGetValue(splayer.PlayerUID, out colors);

                entries.Add(new PlayerPositionEntry
                {
                    Uid = splayer.PlayerUID,
                    Name = splayer.PlayerName,
                    X = splayer.Entity.Pos.X,
                    Y = splayer.Entity.Pos.Y,
                    Z = splayer.Entity.Pos.Z,
                    Yaw = splayer.Entity.Pos.Yaw,
                    FillHex = colors != null ? colors.FillHex : null,
                    BorderHex = colors != null ? colors.BorderHex : null
                });
            }

            // Nobody else to show if fewer than two people are playing.
            if (playing.Count < 2) return;

            PlayerPositionsPacket packet = new PlayerPositionsPacket { Players = entries.ToArray() };
            serverChannel.SendPacket(packet, playing.ToArray());
        }

        private void SendServerSettings(IServerPlayer player)
        {
            if (player == null) return;

            serverChannel.SendPacket(new ServerSettingsPacket
            {
                IsAdmin = player.HasPrivilege(Privilege.controlserver),
                UpdateIntervalMs = serverConfig.UpdateIntervalMs,
                HideCrouchingPlayers = serverConfig.HideCrouchingPlayers,
                ShareWaypoints = serverConfig.ShareWaypoints
            }, player);
        }

        private void OnPlayerColorReceived(IServerPlayer fromPlayer, PlayerColorPacket packet)
        {
            if (fromPlayer == null || packet == null) return;

            playerColors[fromPlayer.PlayerUID] = new PlayerColorPacket
            {
                FillHex = ColorHex.Normalize(packet.FillHex),
                BorderHex = ColorHex.Normalize(packet.BorderHex)
            };
        }

        private void OnSaveServerSettingsReceived(IServerPlayer fromPlayer, SaveServerSettingsPacket packet)
        {
            if (fromPlayer == null || packet == null) return;

            if (!fromPlayer.HasPrivilege(Privilege.controlserver))
            {
                fromPlayer.SendMessage(GlobalConstants.GeneralChatGroup, Lang.Get("farplayermarkers:no-permission"), EnumChatType.CommandError);
                return;
            }

            serverConfig.UpdateIntervalMs = packet.UpdateIntervalMs;
            serverConfig.HideCrouchingPlayers = packet.HideCrouchingPlayers;
            serverConfig.ShareWaypoints = packet.ShareWaypoints;
            serverConfig.Clamp();
            sapi.StoreModConfig(serverConfig, ServerConfigFile);
            StartTicking();

            // Let everyone's game know the new settings.
            foreach (IPlayer player in sapi.World.AllOnlinePlayers)
            {
                IServerPlayer splayer = player as IServerPlayer;
                if (splayer != null && splayer.ConnectionState == EnumClientState.Playing) SendServerSettings(splayer);
            }

            // Re-send waypoints straight away (or an empty list if sharing was turned off).
            lastWaypointSignature = null;
            OnWaypointTick(0);

            fromPlayer.SendMessage(GlobalConstants.GeneralChatGroup, Lang.Get("farplayermarkers:settings-saved"), EnumChatType.CommandSuccess);
            sapi.Logger.Notification("[farplayermarkers] {0} changed server settings: update every {1} ms, hide crouching players: {2}, share waypoints: {3}",
                fromPlayer.PlayerName, serverConfig.UpdateIntervalMs, serverConfig.HideCrouchingPlayers, serverConfig.ShareWaypoints);
        }

        // ---------------- Shared waypoints (server) ----------------

        /// <summary>Collects every player's waypoints from the game's own waypoint layer.</summary>
        private SharedWaypoint[] CollectWaypoints()
        {
            if (!serverConfig.ShareWaypoints) return new SharedWaypoint[0];

            WaypointMapLayer layer = FindWaypointLayer();
            IEnumerable waypoints = layer != null ? ReadList(layer, "Waypoints") : null;
            if (waypoints == null) return new SharedWaypoint[0];

            Dictionary<string, string> names = new Dictionary<string, string>();
            List<SharedWaypoint> list = new List<SharedWaypoint>();

            foreach (object item in waypoints)
            {
                Waypoint wp = item as Waypoint;
                if (wp == null || wp.Position == null || wp.OwningPlayerUid == null) continue;

                string ownerName;
                if (!names.TryGetValue(wp.OwningPlayerUid, out ownerName))
                {
                    ownerName = PlayerNameFor(wp.OwningPlayerUid);
                    names[wp.OwningPlayerUid] = ownerName;
                }

                list.Add(new SharedWaypoint
                {
                    X = wp.Position.X,
                    Y = wp.Position.Y,
                    Z = wp.Position.Z,
                    Title = wp.Title,
                    Icon = wp.Icon,
                    Color = wp.Color,
                    Pinned = wp.Pinned,
                    OwnerUid = wp.OwningPlayerUid,
                    OwnerName = ownerName
                });
            }

            return list.ToArray();
        }

        private WaypointMapLayer waypointLayer;

        /// <summary>Finds the game's own waypoint layer (remembered after the first time).</summary>
        private WaypointMapLayer FindWaypointLayer()
        {
            if (waypointLayer != null) return waypointLayer;

            WorldMapManager mapManager = sapi.ModLoader.GetModSystem<WorldMapManager>();
            IEnumerable layers = ReadList(mapManager, "MapLayers");
            if (layers == null) return null;

            foreach (object layer in layers)
            {
                WaypointMapLayer wl = layer as WaypointMapLayer;
                if (wl != null)
                {
                    waypointLayer = wl;
                    break;
                }
            }
            return waypointLayer;
        }

        /// <summary>
        /// Reads a public list field or property by name, as a plain sequence.
        /// Source mods compiled by the game can't use the game's List&lt;T&gt; members directly
        /// (error CS0012), so they are read this way instead.
        /// </summary>
        private static IEnumerable ReadList(object owner, string name)
        {
            if (owner == null) return null;

            Type type = owner.GetType();
            FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (field != null) return field.GetValue(owner) as IEnumerable;

            PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property != null) return property.GetValue(owner) as IEnumerable;

            return null;
        }

        private string PlayerNameFor(string uid)
        {
            IPlayer online = sapi.World.PlayerByUid(uid);
            if (online != null && online.PlayerName != null) return online.PlayerName;

            try
            {
                var data = sapi.PlayerData.GetPlayerDataByUid(uid);
                if (data != null && data.LastKnownPlayername != null) return data.LastKnownPlayername;
            }
            catch (Exception)
            {
                // Fall through to "Unknown"
            }
            return Lang.Get("farplayermarkers:unknown");
        }

        private static string Signature(SharedWaypoint[] waypoints)
        {
            StringBuilder sb = new StringBuilder();
            foreach (SharedWaypoint w in waypoints)
            {
                sb.Append(w.OwnerUid).Append('|').Append(w.Title).Append('|').Append(w.Icon).Append('|').Append(w.Color).Append('|').Append(w.Pinned)
                  .Append('|').Append((int)w.X).Append(',').Append((int)w.Y).Append(',').Append((int)w.Z).Append(';');
            }
            return sb.ToString();
        }

        /// <summary>Sends the waypoint list to everyone, but only when something changed.</summary>
        private void OnWaypointTick(float dt)
        {
            SharedWaypoint[] waypoints = CollectWaypoints();
            string signature = Signature(waypoints);
            if (signature == lastWaypointSignature) return;
            lastWaypointSignature = signature;

            List<IServerPlayer> playing = new List<IServerPlayer>();
            foreach (IPlayer player in sapi.World.AllOnlinePlayers)
            {
                IServerPlayer splayer = player as IServerPlayer;
                if (splayer != null && splayer.ConnectionState == EnumClientState.Playing) playing.Add(splayer);
            }
            if (playing.Count == 0) return;

            serverChannel.SendPacket(new SharedWaypointsPacket { Waypoints = waypoints }, playing.ToArray());
        }

        private void SendWaypointsTo(IServerPlayer player)
        {
            if (player == null) return;
            serverChannel.SendPacket(new SharedWaypointsPacket { Waypoints = CollectWaypoints() }, player);
        }

        // =====================================================================
        // Client
        // =====================================================================

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;
            ClientConfig = LoadClientConfig(api);

            clientChannel = api.Network.GetChannel(ChannelName);
            clientChannel.SetMessageHandler<PlayerPositionsPacket>(OnPositionsReceived);
            clientChannel.SetMessageHandler<ServerSettingsPacket>(OnServerSettingsReceived);
            clientChannel.SetMessageHandler<SharedWaypointsPacket>(OnWaypointsReceived);

            api.ChatCommands
                .Create("farmarkers")
                .WithDescription(Lang.Get("farplayermarkers:command-desc"))
                .HandleWith(OnSettingsCommand);

            api.Logger.Notification("[farplayermarkers] Client side started");
        }

        private ClientConfig LoadClientConfig(ICoreClientAPI api)
        {
            ClientConfig config = null;
            try
            {
                config = api.LoadModConfig<ClientConfig>(ClientConfigFile);
            }
            catch (Exception e)
            {
                api.Logger.Warning("[farplayermarkers] Could not read {0}, using defaults: {1}", ClientConfigFile, e.Message);
            }

            if (config == null) config = new ClientConfig();
            api.StoreModConfig(config, ClientConfigFile);
            return config;
        }

        private TextCommandResult OnSettingsCommand(TextCommandCallingArgs args)
        {
            if (settingsDialog == null)
            {
                settingsDialog = new GuiDialogFarMarkersSettings(capi, this);
                capi.Gui.RegisterDialog(settingsDialog);
            }
            settingsDialog.Open();
            return TextCommandResult.Success();
        }

        private void OnServerSettingsReceived(ServerSettingsPacket packet)
        {
            if (packet == null) return;

            IsAdmin = packet.IsAdmin;
            ServerUpdateIntervalMs = packet.UpdateIntervalMs;
            ServerHideCrouchingPlayers = packet.HideCrouchingPlayers;
            ServerShareWaypoints = packet.ShareWaypoints;

            // The server is ready for us, so tell it our colours.
            SendOwnColors();
        }

        private void OnPositionsReceived(PlayerPositionsPacket packet)
        {
            KnownPlayers.Clear();
            if (packet == null || packet.Players == null) return;

            long now = capi.World.ElapsedMilliseconds;
            foreach (PlayerPositionEntry entry in packet.Players)
            {
                if (entry == null || entry.Uid == null) continue;
                KnownPlayers[entry.Uid] = new KnownPlayer
                {
                    Uid = entry.Uid,
                    Name = entry.Name,
                    X = entry.X,
                    Y = entry.Y,
                    Z = entry.Z,
                    Yaw = entry.Yaw,
                    FillHex = entry.FillHex,
                    BorderHex = entry.BorderHex,
                    ReceivedAtMs = now
                };
            }
        }

        private void OnWaypointsReceived(SharedWaypointsPacket packet)
        {
            SharedWaypoints = (packet != null && packet.Waypoints != null) ? packet.Waypoints : new SharedWaypoint[0];
        }

        private void SendOwnColors()
        {
            if (clientChannel == null || ClientConfig == null) return;

            clientChannel.SendPacket(new PlayerColorPacket
            {
                FillHex = ClientConfig.OwnFillColorHex,
                BorderHex = ClientConfig.OwnBorderColorHex
            });
        }

        /// <summary>Saves this player's settings, redraws markers and tells the server the new colours.</summary>
        public void SaveClientConfig()
        {
            capi.StoreModConfig(ClientConfig, ClientConfigFile);
            if (ConfigChanged != null) ConfigChanged();
            SendOwnColors();
        }

        /// <summary>Asks the server to change its settings. The server checks the player is an admin.</summary>
        public void SendServerSettings(int updateIntervalMs, bool hideCrouchingPlayers, bool shareWaypoints)
        {
            if (clientChannel == null) return;

            clientChannel.SendPacket(new SaveServerSettingsPacket
            {
                UpdateIntervalMs = updateIntervalMs,
                HideCrouchingPlayers = hideCrouchingPlayers,
                ShareWaypoints = shareWaypoints
            });
        }

        /// <summary>How long a marker stays after the last update before it is hidden.</summary>
        public long StaleAfterMs
        {
            get { return Math.Max(5000, ServerUpdateIntervalMs * 3L); }
        }
    }
}
