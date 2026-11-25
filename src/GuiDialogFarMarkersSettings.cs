using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace FarPlayerMarkers
{
    /// <summary>
    /// Settings window opened with .farmarkers.
    /// Two columns, "Your Marker" and "Others' Markers", each with a live marker preview and
    /// fill/border colours (colour square + Edit button that opens the colour picker).
    /// Below: display options, and for admins the server settings.
    /// </summary>
    public class GuiDialogFarMarkersSettings : GuiDialog
    {
        private readonly FarPlayerMarkersMod mod;
        private GuiDialogColorPicker picker;

        // Values being edited. Copied into the config only on Save & Close.
        private string ownFill, ownBorder, otherFill, otherBorder;
        private bool usePlayerColors, keepAtEdge, showWaypoints;
        private string intervalText;
        private bool hideCrouching, shareWaypoints;

        // Ignore text-change events while the window fills in its own values.
        private bool composing;

        public GuiDialogFarMarkersSettings(ICoreClientAPI capi, FarPlayerMarkersMod mod) : base(capi)
        {
            this.mod = mod;
        }

        public override string ToggleKeyCombinationCode => null;

        public void Open()
        {
            ClientConfig cfg = mod.ClientConfig;
            ownFill = cfg.OwnFillColorHex;
            ownBorder = cfg.OwnBorderColorHex;
            otherFill = cfg.OtherFillColorHex;
            otherBorder = cfg.OtherBorderColorHex;
            usePlayerColors = cfg.UsePlayerSelectedColors;
            keepAtEdge = cfg.KeepPlayersAtMapEdge;
            showWaypoints = cfg.ShowOthersWaypoints;
            intervalText = mod.ServerUpdateIntervalMs.ToString();
            hideCrouching = mod.ServerHideCrouchingPlayers;
            shareWaypoints = mod.ServerShareWaypoints;

            Compose();
            TryOpen();
        }

        private void Compose()
        {
            // Widths grow to fit longer translations; the English sizes are the minimum.
            const double colGap = 30;
            const double swatchSize = 28;
            const double hexWidth = 92;
            const double rowHeight = 38;
            const double toggleButtonWidth = 60;

            string fillLabel = Lang.Get("farplayermarkers:fill-color");
            string borderLabel = Lang.Get("farplayermarkers:border-color");
            double labelWidth = Math.Max(115, Math.Max(TextWidth(fillLabel), TextWidth(borderLabel)) + 10);
            double editWidth = Math.Max(70, TextWidth(Lang.Get("farplayermarkers:edit")) + 26);
            double sideToggleWidth = Math.Max(190, Math.Max(
                TextWidth(Lang.Get("farplayermarkers:use-player-colors")),
                TextWidth(Lang.Get("farplayermarkers:hide-sneaking"))) + 10);

            double colWidth = 335;
            colWidth = Math.Max(colWidth, labelWidth + swatchSize + 8 + hexWidth + 8 + editWidth);
            colWidth = Math.Max(colWidth, sideToggleWidth + toggleButtonWidth);
            colWidth = Math.Max(colWidth, TextWidth(Lang.Get("farplayermarkers:your-marker")) + 10);
            colWidth = Math.Max(colWidth, TextWidth(Lang.Get("farplayermarkers:others-markers")) + 10);

            double toggleLabelWidth = Math.Max(230, Math.Max(
                TextWidth(Lang.Get("farplayermarkers:keep-at-edge")), Math.Max(
                TextWidth(Lang.Get("farplayermarkers:show-waypoints")),
                TextWidth(Lang.Get("farplayermarkers:share-waypoints")))) + 10);
            double rateLabelWidth = Math.Max(150, TextWidth(Lang.Get("farplayermarkers:update-rate")) + 10);
            double cancelWidth = Math.Max(130, TextWidth(Lang.Get("farplayermarkers:cancel")) + 40);
            double saveWidth = Math.Max(170, TextWidth(Lang.Get("farplayermarkers:save-close")) + 40);

            double fullWidth = colWidth * 2 + colGap;
            fullWidth = Math.Max(fullWidth, toggleLabelWidth + toggleButtonWidth);
            fullWidth = Math.Max(fullWidth, cancelWidth + saveWidth + 20);
            double right = colWidth + colGap;
            double top = GuiStyle.TitleBarHeight + 5;

            CairoFont textFont = CairoFont.WhiteSmallText();
            CairoFont headFont = CairoFont.WhiteSmallText().WithWeight(Cairo.FontWeight.Bold);

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;

            GuiComposer composer = capi.Gui
                .CreateCompo("farplayermarkers-settings", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(Lang.Get("farplayermarkers:gui-title"), OnTitleClose)
                .BeginChildElements(bgBounds);

            // Column headers with a live marker preview under each
            composer.AddStaticText(Lang.Get("farplayermarkers:your-marker"), headFont, ElementBounds.Fixed(0, top, colWidth, 25));
            composer.AddStaticText(Lang.Get("farplayermarkers:others-markers"), headFont, ElementBounds.Fixed(right, top, colWidth, 25));
            double y = top + 28;

            composer.AddInteractiveElement(new GuiElementMarkerPreview(capi, ElementBounds.Fixed(labelWidth, y, 48, 48),
                () => ownFill, () => ownBorder), "own-preview");
            composer.AddInteractiveElement(new GuiElementMarkerPreview(capi, ElementBounds.Fixed(right + labelWidth, y, 48, 48),
                () => otherFill, () => otherBorder), "other-preview");
            y += 56;

            // Fill / border rows: label, colour square, Edit button
            AddColorRow(composer, 0, y, labelWidth, swatchSize, hexWidth, editWidth, Lang.Get("farplayermarkers:fill-color"), "ownfill", textFont);
            AddColorRow(composer, right, y, labelWidth, swatchSize, hexWidth, editWidth, Lang.Get("farplayermarkers:fill-color"), "otherfill", textFont);
            y += rowHeight;
            AddColorRow(composer, 0, y, labelWidth, swatchSize, hexWidth, editWidth, Lang.Get("farplayermarkers:border-color"), "ownborder", textFont);
            AddColorRow(composer, right, y, labelWidth, swatchSize, hexWidth, editWidth, Lang.Get("farplayermarkers:border-color"), "otherborder", textFont);
            y += rowHeight;

            // Under the others column: use each player's own colours
            AddToggleRow(composer, right, y, sideToggleWidth, Lang.Get("farplayermarkers:use-player-colors"), usePlayerColors, OnToggleUsePlayerColors, "useplayercolors", textFont);
            y += rowHeight + 6;

            // Display options
            composer.AddStaticText(Lang.Get("farplayermarkers:display"), headFont, ElementBounds.Fixed(0, y, fullWidth, 25));
            y += 32;
            AddToggleRow(composer, 0, y, toggleLabelWidth, Lang.Get("farplayermarkers:keep-at-edge"), keepAtEdge, OnToggleKeepAtEdge, "keepatedge", textFont);
            y += rowHeight;
            AddToggleRow(composer, 0, y, toggleLabelWidth, Lang.Get("farplayermarkers:show-waypoints"), showWaypoints, OnToggleShowWaypoints, "showwaypoints", textFont);
            y += rowHeight + 6;

            // Server settings, admins only
            if (mod.IsAdmin)
            {
                composer.AddStaticText(Lang.Get("farplayermarkers:server-settings-admin"), headFont, ElementBounds.Fixed(0, y, fullWidth, 25));
                y += 32;

                composer.AddStaticText(Lang.Get("farplayermarkers:update-rate"), textFont, ElementBounds.Fixed(0, y + 5, rateLabelWidth, 25));
                composer.AddTextInput(ElementBounds.Fixed(rateLabelWidth, y, 80, 28), OnIntervalChanged, textFont, "interval");
                AddToggleRow(composer, right, y, sideToggleWidth, Lang.Get("farplayermarkers:hide-sneaking"), hideCrouching, OnToggleHideCrouching, "hidecrouching", textFont);
                y += rowHeight;
                AddToggleRow(composer, 0, y, toggleLabelWidth, Lang.Get("farplayermarkers:share-waypoints"), shareWaypoints, OnToggleShareWaypoints, "sharewaypoints", textFont);
                y += rowHeight + 6;
            }

            // Cancel / Save & Close
            composer.AddButton(Lang.Get("farplayermarkers:cancel"), OnCancel, ElementBounds.Fixed(0, y + 10, cancelWidth, 32));
            composer.AddButton(Lang.Get("farplayermarkers:save-close"), OnSave, ElementBounds.Fixed(fullWidth - saveWidth, y + 10, saveWidth, 32));

            composer.EndChildElements();
            SingleComposer = composer.Compose();

            composing = true;
            SingleComposer.GetTextInput("ownfill").SetValue(ownFill);
            SingleComposer.GetTextInput("ownborder").SetValue(ownBorder);
            SingleComposer.GetTextInput("otherfill").SetValue(otherFill);
            SingleComposer.GetTextInput("otherborder").SetValue(otherBorder);
            if (mod.IsAdmin) SingleComposer.GetTextInput("interval").SetValue(intervalText);
            composing = false;
        }

        /// <summary>
        /// Rough width of a line of small GUI text, so labels in longer languages get more room.
        /// Wide (Chinese/Japanese/Korean) characters count about double.
        /// </summary>
        private static double TextWidth(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            double w = 0;
            foreach (char c in text) w += c >= 0x2E80 ? 15 : 7.6;
            return w;
        }

        /// <summary>Label, colour square, hex box (copy/paste or type), Edit button.</summary>
        private void AddColorRow(GuiComposer composer, double x, double y, double labelWidth, double swatchSize, double hexWidth, double editWidth,
            string label, string key, CairoFont font)
        {
            double sx = x + labelWidth;
            double hx = sx + swatchSize + 8;
            double ex = hx + hexWidth + 8;

            composer.AddStaticText(label, font, ElementBounds.Fixed(x, y + 5, labelWidth, 25));
            composer.AddInteractiveElement(new GuiElementColorSwatch(capi,
                ElementBounds.Fixed(sx, y, swatchSize, swatchSize),
                () => ValueFor(key),
                () => OpenPicker(key, label)), key + "-swatch");
            composer.AddTextInput(ElementBounds.Fixed(hx, y, hexWidth, 28), text => OnHexTyped(key, text), font, key);
            composer.AddButton(Lang.Get("farplayermarkers:edit"), () => { OpenPicker(key, label); return true; },
                ElementBounds.Fixed(ex, y - 1, editWidth, 30), EnumButtonStyle.Small, key + "-edit");
        }

        /// <summary>A hex code was typed or pasted into a colour's box.</summary>
        private void OnHexTyped(string key, string text)
        {
            if (composing) return;
            // Keep whatever is typed (the square shows dark red while it isn't a valid colour yet);
            // invalid codes are ignored on Save & Close.
            SetValueFor(key, text);
        }

        private void AddToggleRow(GuiComposer composer, double x, double y, double labelWidth, string label, bool value,
            ActionConsumable onClick, string key, CairoFont font)
        {
            composer.AddStaticText(label, font, ElementBounds.Fixed(x, y + 5, labelWidth, 25));
            composer.AddButton(Lang.Get(value ? "farplayermarkers:yes" : "farplayermarkers:no"), onClick,
                ElementBounds.Fixed(x + labelWidth, y, 60, 28), EnumButtonStyle.Small, key);
        }

        private string ValueFor(string key)
        {
            switch (key)
            {
                case "ownfill": return ownFill;
                case "ownborder": return ownBorder;
                case "otherfill": return otherFill;
                case "otherborder": return otherBorder;
            }
            return null;
        }

        private void SetValueFor(string key, string hex)
        {
            switch (key)
            {
                case "ownfill": ownFill = hex; break;
                case "ownborder": ownBorder = hex; break;
                case "otherfill": otherFill = hex; break;
                case "otherborder": otherBorder = hex; break;
            }
        }

        /// <summary>Opens the colour picker for one colour. Save in the picker writes the colour back here.</summary>
        private void OpenPicker(string key, string label)
        {
            try
            {
                if (picker == null)
                {
                    picker = new GuiDialogColorPicker(capi);
                    capi.Gui.RegisterDialog(picker);
                }

                string section = (key == "ownfill" || key == "ownborder")
                    ? Lang.Get("farplayermarkers:your-marker")
                    : Lang.Get("farplayermarkers:others-markers");
                string title = section + " - " + label.TrimEnd(':');

                picker.Open(ValueFor(key), title, hex =>
                {
                    SetValueFor(key, hex);
                    if (SingleComposer != null)
                    {
                        composing = true;
                        SingleComposer.GetTextInput(key).SetValue(hex);
                        composing = false;
                    }
                });
            }
            catch (Exception e)
            {
                capi.Logger.Error("[farplayermarkers] Could not open the colour picker: {0}", e);
                capi.ShowChatMessage("[Far Player Markers] Could not open the colour picker: " + e.Message);
            }
        }

        private void OnIntervalChanged(string text) { intervalText = text; }

        private bool OnToggleUsePlayerColors() { usePlayerColors = !usePlayerColors; Compose(); return true; }
        private bool OnToggleKeepAtEdge() { keepAtEdge = !keepAtEdge; Compose(); return true; }
        private bool OnToggleShowWaypoints() { showWaypoints = !showWaypoints; Compose(); return true; }
        private bool OnToggleHideCrouching() { hideCrouching = !hideCrouching; Compose(); return true; }
        private bool OnToggleShareWaypoints() { shareWaypoints = !shareWaypoints; Compose(); return true; }

        public override void OnGuiClosed()
        {
            if (picker != null && picker.IsOpened()) picker.TryClose();
            base.OnGuiClosed();
        }

        private bool OnSave()
        {
            ClientConfig cfg = mod.ClientConfig;

            cfg.OwnFillColorHex = ColorHex.Normalize(ownFill) ?? cfg.OwnFillColorHex;
            cfg.OwnBorderColorHex = ColorHex.Normalize(ownBorder) ?? cfg.OwnBorderColorHex;
            cfg.OtherFillColorHex = ColorHex.Normalize(otherFill) ?? cfg.OtherFillColorHex;
            cfg.OtherBorderColorHex = ColorHex.Normalize(otherBorder) ?? cfg.OtherBorderColorHex;
            cfg.UsePlayerSelectedColors = usePlayerColors;
            cfg.KeepPlayersAtMapEdge = keepAtEdge;
            cfg.ShowOthersWaypoints = showWaypoints;
            mod.SaveClientConfig();

            if (mod.IsAdmin)
            {
                int interval;
                if (!int.TryParse(intervalText, out interval)) interval = mod.ServerUpdateIntervalMs;
                mod.SendServerSettings(interval, hideCrouching, shareWaypoints);
            }

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
