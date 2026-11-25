using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace FarPlayerMarkers
{
    /// <summary>
    /// Colour picker window, opened by an Edit button in the settings window.
    /// A saturation/brightness square, a hue slider, a preview (old and new colour)
    /// and a hex box, all kept in sync. Save writes the colour back.
    /// </summary>
    public class GuiDialogColorPicker : GuiDialog
    {
        private readonly ColorState state = new ColorState();
        private Action<string> onPicked;
        private string title;
        private string originalHex = "#FFFFFF";
        private bool updatingText;

        public GuiDialogColorPicker(ICoreClientAPI capi) : base(capi)
        {
        }

        public override string ToggleKeyCombinationCode => null;

        public void Open(string startHex, string title, Action<string> onPicked)
        {
            this.onPicked = onPicked;
            this.title = title;

            string hex = ColorHex.Normalize(startHex) ?? "#FFFFFF";
            if (hex.Length > 7) hex = hex.Substring(0, 7);
            originalHex = hex;
            state.SetFromHex(hex);

            Compose();

            if (!IsOpened() && !TryOpen())
            {
                capi.Logger.Warning("[farplayermarkers] The game refused to open the colour picker window");
                capi.ShowChatMessage("[Far Player Markers] The game refused to open the colour picker window.");
            }
        }

        private void Compose()
        {
            const double boxSize = 200;
            const double hueWidth = 24;
            double hueX = boxSize + 12;
            double rightX = hueX + hueWidth + 20;
            double top = GuiStyle.TitleBarHeight + 5;

            CairoFont textFont = CairoFont.WhiteSmallText();

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;

            GuiComposer composer = capi.Gui
                .CreateCompo("farplayermarkers-colorpicker", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(title ?? Lang.Get("farplayermarkers:pick-color"), OnTitleClose)
                .BeginChildElements(bgBounds);

            composer.AddInteractiveElement(new GuiElementSaturationValueBox(capi, ElementBounds.Fixed(0, top, boxSize, boxSize), state, OnColorChanged), "svbox");
            composer.AddInteractiveElement(new GuiElementHueBar(capi, ElementBounds.Fixed(hueX, top, hueWidth, boxSize), state, OnColorChanged), "huebar");

            // Preview: new colour on top, original colour underneath
            composer.AddStaticText(Lang.Get("farplayermarkers:new"), textFont, ElementBounds.Fixed(rightX, top, 110, 20));
            composer.AddInteractiveElement(new GuiElementColorSwatch(capi, ElementBounds.Fixed(rightX, top + 22, 110, 34), () => state.Hex, null), "preview-new");
            composer.AddStaticText(Lang.Get("farplayermarkers:current"), textFont, ElementBounds.Fixed(rightX, top + 62, 110, 20));
            composer.AddInteractiveElement(new GuiElementColorSwatch(capi, ElementBounds.Fixed(rightX, top + 84, 110, 34), () => originalHex, null), "preview-old");

            composer.AddStaticText(Lang.Get("farplayermarkers:hex"), textFont, ElementBounds.Fixed(rightX, top + 128, 110, 20));
            composer.AddTextInput(ElementBounds.Fixed(rightX, top + 150, 110, 28), OnHexChanged, textFont, "hex");

            double buttonsY = top + boxSize + 14;
            composer.AddButton(Lang.Get("farplayermarkers:cancel"), OnCancel, ElementBounds.Fixed(0, buttonsY, 110, 30));
            composer.AddButton(Lang.Get("farplayermarkers:save"), OnSave, ElementBounds.Fixed(rightX, buttonsY, 110, 30));

            composer.EndChildElements();
            SingleComposer = composer.Compose();

            SetHexText(state.Hex);
        }

        private void SetHexText(string hex)
        {
            if (SingleComposer == null) return;
            updatingText = true;
            SingleComposer.GetTextInput("hex").SetValue(hex);
            updatingText = false;
        }

        /// <summary>The square or hue slider moved: update the hex box.</summary>
        private void OnColorChanged()
        {
            SetHexText(state.Hex);
        }

        /// <summary>A hex code was typed: move the square and slider to match.</summary>
        private void OnHexChanged(string text)
        {
            if (updatingText) return;
            string hex = ColorHex.Normalize(text);
            if (hex == null) return;
            if (hex.Length > 7) hex = hex.Substring(0, 7);
            state.SetFromHex(hex);
        }

        private bool OnSave()
        {
            if (onPicked != null) onPicked(state.Hex);
            TryClose();
            return true;
        }

        private bool OnCancel()
        {
            TryClose();
            return true;
        }

        private void OnTitleClose()
        {
            TryClose();
        }
    }
}
