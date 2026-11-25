using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace FarPlayerMarkers
{
    /// <summary>HSV and RGB conversions used by the colour picker. All values 0-1.</summary>
    public static class ColorMath
    {
        public static void HsvToRgb(double h, double s, double v, out double r, out double g, out double b)
        {
            h = h - Math.Floor(h);
            double i = Math.Floor(h * 6);
            double f = h * 6 - i;
            double p = v * (1 - s);
            double q = v * (1 - f * s);
            double t = v * (1 - (1 - f) * s);

            switch ((int)i % 6)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }
        }

        public static void RgbToHsv(double r, double g, double b, out double h, out double s, out double v)
        {
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double d = max - min;

            v = max;
            s = max <= 0 ? 0 : d / max;

            if (d <= 0) { h = 0; return; }
            if (max == r) h = ((g - b) / d) % 6;
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h /= 6;
            if (h < 0) h += 1;
        }

        public static string ToHex(double r, double g, double b)
        {
            return "#" + Byte(r).ToString("X2") + Byte(g).ToString("X2") + Byte(b).ToString("X2");
        }

        private static int Byte(double x)
        {
            int n = (int)Math.Round(x * 255);
            return n < 0 ? 0 : (n > 255 ? 255 : n);
        }
    }

    /// <summary>The colour being edited, shared by the picker's square and hue slider.</summary>
    public class ColorState
    {
        public double Hue;
        public double Saturation = 1;
        public double Value = 1;

        public string Hex
        {
            get
            {
                double r, g, b;
                ColorMath.HsvToRgb(Hue, Saturation, Value, out r, out g, out b);
                return ColorMath.ToHex(r, g, b);
            }
        }

        public bool SetFromHex(string hex)
        {
            double r, g, b, a, h, s, v;
            if (!ColorHex.TryParse(hex, out r, out g, out b, out a)) return false;
            ColorMath.RgbToHsv(r, g, b, out h, out s, out v);

            // Greys have no hue; keep the current one so the slider doesn't jump.
            if (s > 0.001 && v > 0.001) Hue = h;
            Saturation = s;
            Value = v;
            return true;
        }
    }

    /// <summary>Shared helpers for the picker's custom elements.</summary>
    public abstract class PickerElementBase : GuiElement
    {
        protected readonly ICoreClientAPI capi;
        protected bool dragging;

        protected PickerElementBase(ICoreClientAPI capi, ElementBounds bounds) : base(capi, bounds)
        {
            this.capi = capi;
        }

        /// <summary>Hit test using the same screen coordinates the element is drawn at.</summary>
        protected bool Hit(double x, double y, double margin)
        {
            return x >= Bounds.renderX - margin && x <= Bounds.renderX + Bounds.InnerWidth + margin
                && y >= Bounds.renderY - margin && y <= Bounds.renderY + Bounds.InnerHeight + margin;
        }

        protected LoadedTexture MakeTexture(Cairo.ImageSurface surface, int w, int h, LoadedTexture old)
        {
            if (old != null) old.Dispose();
            int id = capi.Gui.LoadCairoTexture(surface, true);
            return new LoadedTexture(capi, id, w, h);
        }

        protected void Draw(LoadedTexture tex, double x, double y, double w, double h)
        {
            if (tex == null) return;
            capi.Render.Render2DTexturePremultipliedAlpha(tex.TextureId, (float)x, (float)y, (float)w, (float)h);
        }

        public override void OnMouseDown(ICoreClientAPI api, MouseEvent args)
        {
            if (!Hit(args.X, args.Y, 2)) return;
            dragging = true;
            OnPick(args.X, args.Y);
            args.Handled = true;
        }

        public override void OnMouseMove(ICoreClientAPI api, MouseEvent args)
        {
            if (!dragging) return;
            OnPick(args.X, args.Y);
            args.Handled = true;
        }

        public override void OnMouseUp(ICoreClientAPI api, MouseEvent args)
        {
            dragging = false;
            base.OnMouseUp(api, args);
        }

        protected abstract void OnPick(double mouseX, double mouseY);

        protected static double Clamp01(double v)
        {
            return v < 0 ? 0 : (v > 1 ? 1 : v);
        }
    }

    /// <summary>
    /// The big square: left to right goes from white to the full colour (saturation),
    /// top to bottom from bright to black (brightness). Click or drag to choose.
    /// </summary>
    public class GuiElementSaturationValueBox : PickerElementBase
    {
        private readonly ColorState state;
        private readonly Action onChanged;
        private LoadedTexture boxTexture;
        private LoadedTexture ringTexture;
        private double builtForHue = -1;

        public GuiElementSaturationValueBox(ICoreClientAPI capi, ElementBounds bounds, ColorState state, Action onChanged) : base(capi, bounds)
        {
            this.state = state;
            this.onChanged = onChanged;
        }

        public override void ComposeElements(Cairo.Context ctxStatic, Cairo.ImageSurface surfaceStatic)
        {
            Bounds.CalcWorldBounds();
            BuildBox();
            BuildRing();
        }

        private void BuildBox()
        {
            int w = Math.Max(16, (int)Bounds.InnerWidth);
            int h = Math.Max(16, (int)Bounds.InnerHeight);

            double r, g, b;
            ColorMath.HsvToRgb(state.Hue, 1, 1, out r, out g, out b);

            using (Cairo.ImageSurface surface = new Cairo.ImageSurface(Cairo.Format.Argb32, w, h))
            using (Cairo.Context ctx = new Cairo.Context(surface))
            {
                using (Cairo.LinearGradient across = new Cairo.LinearGradient(0, 0, w, 0))
                {
                    across.AddColorStop(0, new Cairo.Color(1, 1, 1, 1));
                    across.AddColorStop(1, new Cairo.Color(r, g, b, 1));
                    ctx.Rectangle(0, 0, w, h);
                    ctx.SetSource(across);
                    ctx.Fill();
                }
                using (Cairo.LinearGradient down = new Cairo.LinearGradient(0, 0, 0, h))
                {
                    down.AddColorStop(0, new Cairo.Color(0, 0, 0, 0));
                    down.AddColorStop(1, new Cairo.Color(0, 0, 0, 1));
                    ctx.Rectangle(0, 0, w, h);
                    ctx.SetSource(down);
                    ctx.Fill();
                }
                ctx.Rectangle(0, 0, w, h);
                ctx.LineWidth = 2;
                ctx.SetSourceRGBA(0, 0, 0, 0.9);
                ctx.Stroke();

                boxTexture = MakeTexture(surface, w, h, boxTexture);
            }
            builtForHue = state.Hue;
        }

        private void BuildRing()
        {
            int px = Math.Max(10, (int)GuiElement.scaled(14));
            double c = px / 2.0;
            using (Cairo.ImageSurface surface = new Cairo.ImageSurface(Cairo.Format.Argb32, px, px))
            using (Cairo.Context ctx = new Cairo.Context(surface))
            {
                ctx.Arc(c, c, c - 3, 0, 2 * Math.PI);
                ctx.LineWidth = 3;
                ctx.SetSourceRGBA(0, 0, 0, 1);
                ctx.StrokePreserve();
                ctx.LineWidth = 1.5;
                ctx.SetSourceRGBA(1, 1, 1, 1);
                ctx.Stroke();
                ringTexture = MakeTexture(surface, px, px, ringTexture);
            }
        }

        public override void RenderInteractiveElements(float deltaTime)
        {
            if (Math.Abs(builtForHue - state.Hue) > 0.0005) BuildBox();

            double x = Bounds.renderX, y = Bounds.renderY, w = Bounds.InnerWidth, h = Bounds.InnerHeight;
            Draw(boxTexture, x, y, w, h);

            if (ringTexture != null)
            {
                double mx = x + state.Saturation * w;
                double my = y + (1 - state.Value) * h;
                double rs = ringTexture.Width;
                Draw(ringTexture, mx - rs / 2, my - rs / 2, rs, rs);
            }
        }

        protected override void OnPick(double mouseX, double mouseY)
        {
            state.Saturation = Clamp01((mouseX - Bounds.renderX) / Bounds.InnerWidth);
            state.Value = 1 - Clamp01((mouseY - Bounds.renderY) / Bounds.InnerHeight);
            if (onChanged != null) onChanged();
        }

        public override void Dispose()
        {
            if (boxTexture != null) boxTexture.Dispose();
            if (ringTexture != null) ringTexture.Dispose();
            base.Dispose();
        }
    }

    /// <summary>A vertical rainbow slider that picks the hue. Click or drag to choose.</summary>
    public class GuiElementHueBar : PickerElementBase
    {
        private readonly ColorState state;
        private readonly Action onChanged;
        private LoadedTexture barTexture;
        private LoadedTexture markerTexture;

        public GuiElementHueBar(ICoreClientAPI capi, ElementBounds bounds, ColorState state, Action onChanged) : base(capi, bounds)
        {
            this.state = state;
            this.onChanged = onChanged;
        }

        public override void ComposeElements(Cairo.Context ctxStatic, Cairo.ImageSurface surfaceStatic)
        {
            Bounds.CalcWorldBounds();

            int w = Math.Max(8, (int)Bounds.InnerWidth);
            int h = Math.Max(16, (int)Bounds.InnerHeight);

            using (Cairo.ImageSurface surface = new Cairo.ImageSurface(Cairo.Format.Argb32, w, h))
            using (Cairo.Context ctx = new Cairo.Context(surface))
            using (Cairo.LinearGradient grad = new Cairo.LinearGradient(0, 0, 0, h))
            {
                for (int i = 0; i <= 6; i++)
                {
                    double r, g, b;
                    ColorMath.HsvToRgb(i / 6.0, 1, 1, out r, out g, out b);
                    grad.AddColorStop(i / 6.0, new Cairo.Color(r, g, b, 1));
                }
                ctx.Rectangle(0, 0, w, h);
                ctx.SetSource(grad);
                ctx.FillPreserve();
                ctx.LineWidth = 2;
                ctx.SetSourceRGBA(0, 0, 0, 0.9);
                ctx.Stroke();

                barTexture = MakeTexture(surface, w, h, barTexture);
            }

            int mw = w + 8;
            int mh = Math.Max(6, (int)GuiElement.scaled(6));
            using (Cairo.ImageSurface surface = new Cairo.ImageSurface(Cairo.Format.Argb32, mw, mh))
            using (Cairo.Context ctx = new Cairo.Context(surface))
            {
                ctx.Rectangle(1, 1, mw - 2, mh - 2);
                ctx.SetSourceRGBA(1, 1, 1, 1);
                ctx.FillPreserve();
                ctx.LineWidth = 2;
                ctx.SetSourceRGBA(0, 0, 0, 1);
                ctx.Stroke();
                markerTexture = MakeTexture(surface, mw, mh, markerTexture);
            }
        }

        public override void RenderInteractiveElements(float deltaTime)
        {
            double x = Bounds.renderX, y = Bounds.renderY, w = Bounds.InnerWidth, h = Bounds.InnerHeight;
            Draw(barTexture, x, y, w, h);

            if (markerTexture != null)
            {
                double my = y + state.Hue * h - markerTexture.Height / 2.0;
                Draw(markerTexture, x - 4, my, markerTexture.Width, markerTexture.Height);
            }
        }

        protected override void OnPick(double mouseX, double mouseY)
        {
            double h = Clamp01((mouseY - Bounds.renderY) / Bounds.InnerHeight);
            state.Hue = h >= 1 ? 0.9999 : h;
            if (onChanged != null) onChanged();
        }

        public override void Dispose()
        {
            if (barTexture != null) barTexture.Dispose();
            if (markerTexture != null) markerTexture.Dispose();
            base.Dispose();
        }
    }

    /// <summary>A colour square that always shows the current colour. Optionally clickable.</summary>
    public class GuiElementColorSwatch : PickerElementBase
    {
        private readonly Func<string> getHex;
        private readonly Action onClick;
        private LoadedTexture texture;
        private string builtFor;

        public GuiElementColorSwatch(ICoreClientAPI capi, ElementBounds bounds, Func<string> getHex, Action onClick) : base(capi, bounds)
        {
            this.getHex = getHex;
            this.onClick = onClick;
        }

        public override void ComposeElements(Cairo.Context ctxStatic, Cairo.ImageSurface surfaceStatic)
        {
            Bounds.CalcWorldBounds();
            Build();
        }

        private void Build()
        {
            string hex = getHex != null ? getHex() : null;
            int w = Math.Max(8, (int)Bounds.InnerWidth);
            int h = Math.Max(8, (int)Bounds.InnerHeight);

            using (Cairo.ImageSurface surface = new Cairo.ImageSurface(Cairo.Format.Argb32, w, h))
            using (Cairo.Context ctx = new Cairo.Context(surface))
            {
                double r, g, b, a;
                ctx.Rectangle(1, 1, w - 2, h - 2);
                if (ColorHex.TryParse(hex, out r, out g, out b, out a)) ctx.SetSourceRGBA(r, g, b, a);
                else ctx.SetSourceRGBA(0.4, 0, 0, 1); // invalid colour: dark red
                ctx.FillPreserve();
                ctx.LineWidth = 2;
                ctx.SetSourceRGBA(0, 0, 0, 1);
                ctx.Stroke();

                texture = MakeTexture(surface, w, h, texture);
            }
            builtFor = hex;
        }

        public override void RenderInteractiveElements(float deltaTime)
        {
            string hex = getHex != null ? getHex() : null;
            if (hex != builtFor) Build();
            Draw(texture, Bounds.renderX, Bounds.renderY, Bounds.InnerWidth, Bounds.InnerHeight);
        }

        public override void OnMouseDown(ICoreClientAPI api, MouseEvent args)
        {
            if (onClick == null || !Hit(args.X, args.Y, 0)) return;
            args.Handled = true;
            onClick();
        }

        protected override void OnPick(double mouseX, double mouseY) { }

        public override void Dispose()
        {
            if (texture != null) texture.Dispose();
            base.Dispose();
        }
    }

    /// <summary>A preview of a map marker (dot with a pointer) in the given fill and border colours.</summary>
    public class GuiElementMarkerPreview : PickerElementBase
    {
        private readonly Func<string> getFill;
        private readonly Func<string> getBorder;
        private LoadedTexture texture;
        private string builtFor;

        public GuiElementMarkerPreview(ICoreClientAPI capi, ElementBounds bounds, Func<string> getFill, Func<string> getBorder) : base(capi, bounds)
        {
            this.getFill = getFill;
            this.getBorder = getBorder;
        }

        public override void ComposeElements(Cairo.Context ctxStatic, Cairo.ImageSurface surfaceStatic)
        {
            Bounds.CalcWorldBounds();
            Build();
        }

        private string Key()
        {
            return (getFill != null ? getFill() : "") + "|" + (getBorder != null ? getBorder() : "");
        }

        private void Build()
        {
            int px = Math.Max(16, (int)Math.Min(Bounds.InnerWidth, Bounds.InnerHeight));
            double c = px / 2.0;
            double radius = px * 0.28;
            double line = Math.Max(1.5, px / 12.0);

            double fr, fg, fb, fa, br, bg, bb, ba;
            if (!ColorHex.TryParse(getFill != null ? getFill() : null, out fr, out fg, out fb, out fa)) { fr = 0.4; fg = 0; fb = 0; fa = 1; }
            if (!ColorHex.TryParse(getBorder != null ? getBorder() : null, out br, out bg, out bb, out ba)) { br = 0; bg = 0; bb = 0; ba = 1; }

            using (Cairo.ImageSurface surface = new Cairo.ImageSurface(Cairo.Format.Argb32, px, px))
            using (Cairo.Context ctx = new Cairo.Context(surface))
            {
                ctx.MoveTo(c, line);
                ctx.LineTo(c + radius * 0.85, c - radius * 0.35);
                ctx.LineTo(c - radius * 0.85, c - radius * 0.35);
                ctx.ClosePath();
                ctx.SetSourceRGBA(fr, fg, fb, fa);
                ctx.FillPreserve();
                ctx.LineWidth = line;
                ctx.SetSourceRGBA(br, bg, bb, ba);
                ctx.Stroke();

                ctx.NewPath();
                ctx.Arc(c, c, radius, 0, 2 * Math.PI);
                ctx.SetSourceRGBA(fr, fg, fb, fa);
                ctx.FillPreserve();
                ctx.LineWidth = line;
                ctx.SetSourceRGBA(br, bg, bb, ba);
                ctx.Stroke();

                texture = MakeTexture(surface, px, px, texture);
            }
            builtFor = Key();
        }

        public override void RenderInteractiveElements(float deltaTime)
        {
            if (Key() != builtFor) Build();
            if (texture == null) return;
            double s = texture.Width;
            Draw(texture, Bounds.renderX + (Bounds.InnerWidth - s) / 2, Bounds.renderY + (Bounds.InnerHeight - s) / 2, s, s);
        }

        // Display only: ignore clicks.
        public override void OnMouseDown(ICoreClientAPI api, MouseEvent args) { }

        protected override void OnPick(double mouseX, double mouseY) { }

        public override void Dispose()
        {
            if (texture != null) texture.Dispose();
            base.Dispose();
        }
    }
}
