using System.Windows;
using System.Windows.Controls;
using NMSE.Core.Utilities;
using NMSE.Data;
using NMSE.Models;
using NMSE.Next.Engine;

namespace NMSE.Next.Desktop;

internal sealed partial class DomainPageView
{
    private JsonArray? Pets => _session.Player?.GetArray("Pets");

    private void ShowCompanionAccessories(JsonObject? pet, int index)
    {
        if (pet == null || !_session.LoadAccessoryCatalog())
        {
            AddAccessoryMessage(UiStrings.Get("companion.accessory_catalog_unavailable"));
            return;
        }

        var descriptors = pet.GetArray("Descriptors");
        var parts = descriptors == null ? null : Enumerable.Range(0, descriptors.Length)
            .Select(i => (descriptors.GetString(i) ?? "").TrimStart('^')).ToList();
        var layout = CompanionAccessoryDatabase.GetSlotLayoutForCreature(pet.GetString("CreatureID") ?? "", parts);
        var slots = _session.Player?.GetArray("PetAccessoryCustomisation")?.GetObject(index)?.GetArray("Data");
        if (layout.Length == 0)
        {
            AddAccessoryMessage(UiStrings.Get("companion.no_accessory_slots"));
            return;
        }

        for (int i = 0; i < layout.Length; i++)
        {
            int slotIndex = i;
            var card = new StackPanel();
            card.Children.Add(new TextBlock { Text = layout[i].ToString(), Style = PageStyle("Heading"), FontSize = 17 });
            _form.Children.Add(new Border { Style = PageStyle("Card"), Child = card });
            var slot = slots != null && i < slots.Length ? slots.GetObject(i) : null;
            var data = slot?.GetObject("CustomData");
            if (data?.GetArray("DescriptorGroups") is not { } groups)
            {
                card.Children.Add(new TextBlock { Text = UiStrings.Get("companion.accessory_slot_missing") });
                continue;
            }

            string? id = groups.Length > 0 ? groups.GetString(0)?.TrimStart('^') : null;
            bool isDefault = slot!.GetString("SelectedPreset") == "^DEFAULT_PET";
            var choices = new List<object> { UiStrings.Get("companion.accessory_none") };
            choices.AddRange(CompanionAccessoryDatabase.GetEntriesForSlot(layout[i]).Where(item => item.Id != "PET_ACC_NULL"));
            var current = isDefault ? choices[0] : choices.OfType<CompanionAccessoryEntry>()
                .FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (current == null)
            {
                current = string.IsNullOrEmpty(id) ? UiStrings.Get("companion.accessory_unknown_preset") : $"{UiStrings.Get("companion.accessory_unknown_preset")}: {id}";
                choices.Add(current);
            }

            var picker = new ComboBox { ItemsSource = choices, SelectedItem = current, MinWidth = 160 };
            card.Children.Add(picker);
            void Equip(CompanionAccessoryEntry? accessory)
            {
                if (!Commit()) { picker.SelectedItem = current; return; }
                try
                {
                    _session.EquipCompanionAccessory(slots!, slotIndex, accessory);
                    _owner.PageStatus(UiStrings.Get("companion.accessory_updated"));
                    Select(_entry!);
                }
                catch (Exception ex) { _owner.PageError(ex); }
            }
            picker.SelectionChanged += (_, _) =>
            {
                if (!Equals(picker.SelectedItem, current))
                    Equip(picker.SelectedItem as CompanionAccessoryEntry);
            };

            if (!isDefault)
            {
                if (data.Get("Scale") is { } scale)
                    AddField(card, data, "Scale", "Scale", scale);
                AddAccessoryColours(card, data.GetArray("Colours"));
                var reset = new Button { Content = "Reset accessory", HorizontalAlignment = HorizontalAlignment.Left };
                reset.Click += (_, _) => Equip(null);
                card.Children.Add(reset);
            }
        }
    }

    private void AddAccessoryColours(Panel card, JsonArray? colours)
    {
        if (colours == null) return;
        NmsColourPalette.LoadShipPalettes(System.IO.Path.Combine(_session.ResourceRoot, "json", "Colour Palettes.json"));
        var palette = NmsColourPalette.GetPaletteColours("PET") ?? NmsColourPalette.PaintPalette;
        for (int i = 0; i < Math.Min(2, colours.Length); i++)
        {
            if (colours.GetObject(i)?.GetArray("Colour") is not { Length: >= 4 } rgba) continue;
            var picker = new ComboBox
            {
                ItemsSource = palette, DisplayMemberPath = "Name",
                ToolTip = i == 0 ? UiStrings.Get("companion.accessory_primary_colour") : UiStrings.Get("companion.accessory_alternate_colour")
            };
            card.Children.Add(new TextBlock { Text = (string)picker.ToolTip, Style = PageStyle("Label") });
            card.Children.Add(picker);
            picker.SelectionChanged += (_, _) =>
            {
                if (picker.SelectedItem is not NmsColourPalette.PaletteEntry choice || !Commit()) return;
                try
                {
                    var values = NmsColourPalette.ToNormalisedRgba(choice.Colour);
                    _session.ApplyFields(Enumerable.Range(0, 4).Select(channel =>
                        new FieldEdit(rgba, channel, values[channel].ToString(System.Globalization.CultureInfo.InvariantCulture),
                            SaveSession.FieldText(rgba.Get(channel)))), false, null);
                    _owner.PageStatus(UiStrings.Get("companion.accessory_colour_updated"));
                }
                catch (Exception ex) { _owner.PageError(ex); }
            };
        }
    }

    private void AddAccessoryMessage(string message) =>
        _form.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 18, 6, 18) });
}
