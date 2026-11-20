using System.Globalization;
using System.IO;
using DrawingColor = System.Drawing.Color;
using NMSE.Core;
using NMSE.Core.Utilities;
using NMSE.Data;
using NMSE.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NMSE.Next.Desktop;

public partial class MainWindow
{
    sealed record CustomPartChoice(string Label, ShipCustomisationItem? Item)
    {
        public override string ToString() => Label;
    }

    sealed record CustomColourChoice(string Label, DrawingColor Colour)
    {
        public override string ToString() => Label;
    }

    Border BuildStarshipCustomisationView()
    {
        var card = new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(12) };
        var body = new StackPanel();
        card.Child = body;

        if (_selectedShip == null)
        {
            body.Children.Add(new TextBlock { Text = "Select an owned ship to customise.", FontSize = 16 });
            return card;
        }

        var player = _session.Player;
        var resource = _selectedShip.Ownership.GetObject("Resource");
        string currentResource = resource?.GetString("Filename") ?? "";
        int ccdIndex = _showArchive ? -1 : StarshipLogic.ShipIndexToCcdIndex(_selectedShip.Index);
        var ccdArray = player?.GetArray("CharacterCustomisationData");
        JsonObject? ccd = ccdIndex >= 0 && ccdArray != null && ccdIndex < ccdArray.Length
            ? ccdArray.GetObject(ccdIndex) : null;
        var customData = ccd?.GetObject("CustomData");
        bool canEdit = !_showArchive && customData != null;

        body.Children.Add(new TextBlock { Text = "Customisation", Style = (Style)FindResource("Heading"), Margin = new Thickness(0, 0, 0, 8) });
        body.Children.Add(new TextBlock
        {
            Text = "Some combinations may not work together. Palettes and textures can override individual colour slots.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.DarkOrange,
            Margin = new Thickness(0, 0, 0, 10)
        });

        ShipCustomisationDatabase.LoadFromFile(Path.Combine(_session.ResourceRoot, "json", "Ship Customisation.json"));
        NmsColourPalette.LoadShipPalettes(Path.Combine(_session.ResourceRoot, "json", "Colour Palettes.json"));

        var resources = ShipCustomisationDatabase.AllResourcePaths;
        var sceneCombo = new ComboBox { MinWidth = 280, IsEnabled = !_showArchive };
        foreach (var path in resources) sceneCombo.Items.Add(path);
        if (!string.IsNullOrWhiteSpace(currentResource) && !sceneCombo.Items.Cast<object>().Any(x => string.Equals(x?.ToString(), currentResource, StringComparison.OrdinalIgnoreCase)))
            sceneCombo.Items.Add(currentResource);
        sceneCombo.SelectedItem = sceneCombo.Items.Cast<object>().FirstOrDefault(x => string.Equals(x?.ToString(), currentResource, StringComparison.OrdinalIgnoreCase));
        AddCustomisationField(body, "Ship Scene (Model) Resource", sceneCombo);
        sceneCombo.SelectionChanged += (_, _) =>
        {
            string? next = sceneCombo.SelectedItem?.ToString();
            if (string.IsNullOrWhiteSpace(next) || resource == null || _showArchive ||
                string.Equals(next, resource.GetString("Filename"), StringComparison.OrdinalIgnoreCase)) return;
            resource.Set("Filename", next);
            _session.MarkDirty();
            RefreshShipDetails();
            _refreshShipList?.Invoke();
            Status.Text = UiStrings.LocalizeText("Ship scene updated in memory. Press Save to keep the change.");
            _exoContent = null;
            ShowExosuitPage(3);
        };

        if (_showArchive)
        {
            body.Children.Add(new TextBlock { Text = "Archived ships must be restored before their customisation can be edited.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 10, 4, 0) });
            return card;
        }

        var config = ShipCustomisationDatabase.GetConfigByResource(currentResource);
        if (config == null)
        {
            body.Children.Add(new TextBlock { Text = "No customisation options are available for this ship scene.", Margin = new Thickness(4, 10, 4, 0) });
            if (!canEdit) body.Children.Add(new TextBlock { Text = "This ship has no editable customisation record in the save.", Margin = new Thickness(4, 4, 4, 0) });
            return card;
        }
        if (!canEdit)
        {
            body.Children.Add(new TextBlock { Text = "This ship has no editable customisation record in the save.", Margin = new Thickness(4, 10, 4, 0) });
            return card;
        }

        var loading = true;
        var refreshingColours = false;
        var slotControls = new List<(ComboBox Combo, ShipCustomisationSlot Slot)>();
        var textureControls = new List<(ComboBox Combo, string GroupId)>();
        var colourControls = new List<(ComboBox Combo, string PaletteName, string ColourAlt, string DisplayPaletteId)>();

        if (config.Slots.Count > 0)
        {
            AddCustomisationHeading(body, "Parts");
            foreach (var slot in config.Slots)
            {
                var combo = new ComboBox { MinWidth = 280 };
                combo.Items.Add(new CustomPartChoice("(None)", null));
                foreach (var item in slot.Items) combo.Items.Add(new CustomPartChoice(item.ItemID, item));
                AddCustomisationField(body, slot.Label, combo);
                slotControls.Add((combo, slot));
            }
        }

        if (config.TextureGroups.Count > 0)
        {
            AddCustomisationHeading(body, "Texture Options");
            foreach (var group in config.TextureGroups)
            {
                var combo = new ComboBox { MinWidth = 280 };
                combo.Items.Add("(None)");
                foreach (var option in group.Options) combo.Items.Add(option);
                AddCustomisationField(body, group.GroupID, combo);
                textureControls.Add((combo, group.GroupID));
            }
        }

        ComboBox? paletteCombo = null;
        if (config.PaletteIDs.Count > 0)
        {
            AddCustomisationHeading(body, "Colours");
            paletteCombo = new ComboBox { MinWidth = 280 };
            var paletteIds = config.PaletteIDs.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (!paletteIds.Contains("SHIP", StringComparer.OrdinalIgnoreCase)) paletteIds.Insert(0, "SHIP");
            foreach (var id in paletteIds) paletteCombo.Items.Add(id);
            var stored = customData!.GetString("PaletteID") ?? "";
            stored = stored.TrimStart('^');
            paletteCombo.SelectedItem = paletteIds.FirstOrDefault(x => string.Equals(x, string.IsNullOrEmpty(stored) ? "SHIP" : stored, StringComparison.OrdinalIgnoreCase)) ?? paletteIds[0];
            AddCustomisationField(body, "Paint Palette", paletteCombo);
        }

        var standardColours = new[]
        {
            ("Paint", "Primary", ""),
            ("Paint", "Alternative3", ""),
            ("Undercoat", "Alternative1", ""),
            ("Paint", "Alternative1", ""),
            ("Paint", "Alternative2", "")
        };
        foreach (var (name, alt, _) in standardColours)
            AddColourControl(name, alt, "", ColourLabel(name, alt));
        foreach (var extra in config.ExtraColourChannels)
            AddColourControl(extra.PaletteName, extra.ColourAlt, extra.DisplayPaletteId,
                string.IsNullOrWhiteSpace(extra.LabelKey) ? $"{extra.PaletteName} {extra.ColourAlt}" : extra.LabelKey);

        void AddColourControl(string paletteName, string colourAlt, string displayPaletteId, string label)
        {
            var combo = new ComboBox { MinWidth = 280 };
            AddCustomisationField(body, label, combo);
            colourControls.Add((combo, paletteName, colourAlt, displayPaletteId));
        }

        var descriptorArray = customData!.GetArray("DescriptorGroups");
        var knownDescriptorIds = config.Slots.SelectMany(x => x.Items).SelectMany(x => x.DescriptorGroupIDs)
            .Select(x => x.TrimStart('^')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (combo, slot) in slotControls)
        {
            int selected = 0;
            if (descriptorArray != null)
            {
                var present = Enumerable.Range(0, descriptorArray.Length)
                    .Select(i => (descriptorArray.Get(i)?.ToString() ?? "").TrimStart('^'))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                for (int i = 1; i < combo.Items.Count; i++)
                    if (combo.Items[i] is CustomPartChoice { Item: not null } choice &&
                        choice.Item.DescriptorGroupIDs.Any(id => present.Contains(id.TrimStart('^'))))
                    { selected = i; break; }
            }
            combo.SelectedIndex = selected;
            combo.SelectionChanged += (_, _) =>
            {
                if (loading || descriptorArray == null) return;
                var preserve = Enumerable.Range(0, descriptorArray.Length)
                    .Select(i => descriptorArray.Get(i)?.ToString() ?? "")
                    .Where(id => !knownDescriptorIds.Contains(id.TrimStart('^'))).ToArray();
                for (int i = descriptorArray.Length - 1; i >= 0; i--) descriptorArray.RemoveAt(i);
                foreach (var id in preserve) descriptorArray.Add(id);
                var written = preserve.Select(id => id.TrimStart('^')).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var (slotCombo, _) in slotControls)
                    if (slotCombo.SelectedItem is CustomPartChoice { Item: not null } choice)
                        foreach (var id in choice.Item.DescriptorGroupIDs)
                            if (written.Add(id.TrimStart('^'))) descriptorArray.Add("^" + id.TrimStart('^'));
                _session.MarkDirty();
                Status.Text = UiStrings.LocalizeText("Ship parts updated in memory. Press Save to keep the changes.");
            };
        }

        var textureArray = customData.GetArray("TextureOptions");
        foreach (var (combo, groupId) in textureControls)
        {
            string current = "";
            if (textureArray != null)
                for (int i = 0; i < textureArray.Length; i++)
                {
                    var entry = textureArray.GetObject(i);
                    if (string.Equals((entry?.GetString("TextureOptionGroupName") ?? "").TrimStart('^'), groupId, StringComparison.OrdinalIgnoreCase))
                        current = (entry?.GetString("TextureOptionName") ?? "").TrimStart('^');
                }
            combo.SelectedItem = combo.Items.Cast<object>().FirstOrDefault(x => string.Equals(x?.ToString(), current, StringComparison.OrdinalIgnoreCase)) ?? combo.Items[0];
            combo.SelectionChanged += (_, _) =>
            {
                if (loading || textureArray == null) return;
                for (int i = textureArray.Length - 1; i >= 0; i--)
                {
                    var entry = textureArray.GetObject(i);
                    if (string.Equals((entry?.GetString("TextureOptionGroupName") ?? "").TrimStart('^'), groupId, StringComparison.OrdinalIgnoreCase))
                        textureArray.RemoveAt(i);
                }
                string selected = combo.SelectedItem?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(selected) && selected != "(None)")
                {
                    var entry = new JsonObject();
                    entry.Set("TextureOptionGroupName", "^" + groupId.TrimStart('^'));
                    entry.Set("TextureOptionName", "^" + selected.TrimStart('^'));
                    textureArray.Add(entry);
                }
                _session.MarkDirty();
                Status.Text = UiStrings.LocalizeText("Ship textures updated in memory. Press Save to keep the changes.");
            };
        }

        void RefreshColourControls()
        {
            refreshingColours = true;
            foreach (var (combo, paletteName, colourAlt, displayPaletteId) in colourControls)
            {
                var currentColour = ReadShipCustomColour(customData.GetArray("Colours"), paletteName, colourAlt);
                if (currentColour == null && paletteName.Equals("Undercoat", StringComparison.OrdinalIgnoreCase) && colourAlt.Equals("Alternative1", StringComparison.OrdinalIgnoreCase))
                    currentColour = ReadShipCustomColour(customData.GetArray("Colours"), "Undercoat", "Primary");
                string paletteId = string.IsNullOrWhiteSpace(displayPaletteId) ? paletteCombo?.SelectedItem?.ToString() ?? "SHIP" : displayPaletteId;
                var palette = NmsColourPalette.GetPaletteColours(paletteId) ?? NmsColourPalette.PaintPalette;
                combo.Items.Clear();
                foreach (var entry in palette)
                    combo.Items.Add(new CustomColourChoice($"{entry.Name}  #{entry.Colour.R:X2}{entry.Colour.G:X2}{entry.Colour.B:X2}", entry.Colour));
                if (combo.Items.Count == 0) continue;
                int best = currentColour.HasValue ? ClosestColourIndex(palette, currentColour.Value) : 0;
                combo.SelectedIndex = Math.Clamp(best, 0, combo.Items.Count - 1);
                if (combo.SelectedItem is CustomColourChoice choice)
                    combo.Background = new SolidColorBrush(Color.FromRgb(choice.Colour.R, choice.Colour.G, choice.Colour.B));
            }
            refreshingColours = false;
        }

        if (paletteCombo != null)
        {
            paletteCombo.SelectionChanged += (_, _) =>
            {
                if (loading) return;
                if (customData.Names().Contains("PaletteID", StringComparer.Ordinal))
                    customData.Set("PaletteID", string.Equals(paletteCombo.SelectedItem?.ToString(), "SHIP", StringComparison.OrdinalIgnoreCase)
                        ? "^" : "^" + (paletteCombo.SelectedItem?.ToString() ?? ""));
                RefreshColourControls();
                _session.MarkDirty();
                Status.Text = UiStrings.LocalizeText("Ship palette updated in memory. Press Save to keep the changes.");
            };
        }

        RefreshColourControls();
        foreach (var (combo, paletteName, colourAlt, _) in colourControls)
        {
            combo.SelectionChanged += (_, _) =>
            {
                if (loading || refreshingColours || combo.SelectedItem is not CustomColourChoice choice) return;
                if (WriteShipCustomColour(customData.GetArray("Colours"), paletteName, colourAlt, choice.Colour))
                {
                    combo.Background = new SolidColorBrush(Color.FromRgb(choice.Colour.R, choice.Colour.G, choice.Colour.B));
                    _session.MarkDirty();
                    Status.Text = UiStrings.LocalizeText("Ship colour updated in memory. Press Save to keep the change.");
                }
            };
        }
        loading = false;
        return card;
    }

    static void AddCustomisationHeading(Panel host, string text)
    {
        host.Children.Add(new TextBlock { Text = text, FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 14, 4, 5) });
    }

    void AddCustomisationField(Panel host, string label, FrameworkElement input)
    {
        var grid = new Grid { Margin = new Thickness(3, 3, 3, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(225) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var caption = new TextBlock { Text = UiStrings.GetOrNull(label) ?? UiStrings.LocalizeText(label), Style = (Style)FindResource("Label"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 8, 0) };
        input.Margin = new Thickness(2);
        Grid.SetColumn(input, 1);
        grid.Children.Add(caption);
        grid.Children.Add(input);
        host.Children.Add(grid);
    }

    static string ColourLabel(string palette, string alt)
    {
        string altLabel = alt.StartsWith("Alternative", StringComparison.OrdinalIgnoreCase) ? "Alternative " + alt["Alternative".Length..] : alt;
        return $"{UiStrings.LocalizeText(palette)} · {UiStrings.LocalizeText(altLabel)}";
    }

    static int ClosestColourIndex(IReadOnlyList<NmsColourPalette.PaletteEntry> palette, DrawingColor target)
    {
        int index = 0;
        long distance = long.MaxValue;
        for (int i = 0; i < palette.Count; i++)
        {
            var colour = palette[i].Colour;
            long dr = colour.R - target.R, dg = colour.G - target.G, db = colour.B - target.B;
            long candidate = dr * dr + dg * dg + db * db;
            if (candidate < distance) { distance = candidate; index = i; }
        }
        return index;
    }

    static DrawingColor? ReadShipCustomColour(JsonArray? colours, string paletteName, string colourAlt)
    {
        if (colours == null) return null;
        for (int i = 0; i < colours.Length; i++)
        {
            var entry = colours.GetObject(i);
            var palette = entry?.GetObject("Palette");
            if (palette == null ||
                !string.Equals(palette.GetString("Palette"), paletteName, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(palette.GetString("ColourAlt"), colourAlt, StringComparison.OrdinalIgnoreCase)) continue;
            var rgba = entry!.GetArray("Colour");
            if (rgba == null || rgba.Length < 3) return null;
            try
            {
                int channel(int index) => Math.Clamp((int)Math.Round(rgba.GetDouble(index) * 255), 0, 255);
                return DrawingColor.FromArgb(channel(0), channel(1), channel(2));
            }
            catch { return null; }
        }
        return null;
    }

    static bool WriteShipCustomColour(JsonArray? colours, string paletteName, string colourAlt, DrawingColor colour)
    {
        if (colours == null) return false;
        for (int i = 0; i < colours.Length; i++)
        {
            var entry = colours.GetObject(i);
            var palette = entry?.GetObject("Palette");
            if (palette == null ||
                !string.Equals(palette.GetString("Palette"), paletteName, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(palette.GetString("ColourAlt"), colourAlt, StringComparison.OrdinalIgnoreCase)) continue;
            var rgba = entry!.GetArray("Colour");
            if (rgba == null || rgba.Length < 4) return false;
            var values = NmsColourPalette.ToNormalisedRgba(colour);
            for (int c = 0; c < 4; c++) rgba.Set(c, values[c]);
            return true;
        }
        return false;
    }
}
