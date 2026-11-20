using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using NMSE.Models;
using NMSE.Next.Engine;

namespace NMSE.Next.Desktop;

internal sealed partial class DomainPageView
{
    private sealed record DescriptorChoice(string Id, string Name, List<DescriptorGroup> Children)
    {
        public override string ToString() => Name;
    }
    private sealed record DescriptorGroup(string Id, List<DescriptorChoice> Options);
    private readonly List<ComboBox> _descriptorPickers = [];

    private void ShowCompanionDescriptors(JsonObject pet)
    {
        _descriptorPickers.Clear();
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(4, 3, 4, 9),
            VerticalAlignment = VerticalAlignment.Center
        };
        var creatureBuilder = new Button
        {
            Content = "Creature Builder (Web)",
            HorizontalAlignment = HorizontalAlignment.Right,
            ToolTip = "Open the NMSCD Creature Builder in your browser"
        };
        creatureBuilder.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://creature.nmscd.com/") { UseShellExecute = true });
            }
            catch (Exception ex) { _owner.PageError(ex); }
        };
        header.Children.Add(new TextBlock
        {
            Text = "Descriptors (Parts)",
            Style = PageStyle("Heading"),
            FontSize = 17,
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        creatureBuilder.Margin = new Thickness(0);
        header.Children.Add(creatureBuilder);
        _form.Children.Add(header);

        var descriptors = pet.GetArray("Descriptors");
        if (descriptors is null)
        {
            AddDescriptorMessage("This companion has no Descriptors array in the save.");
            return;
        }

        string creatureId = (pet.GetString("CreatureID") ?? string.Empty).TrimStart('^');
        List<DescriptorGroup> groups;
        try
        {
            groups = LoadDescriptorGroups(creatureId);
        }
        catch (Exception ex)
        {
            _owner.PageError(ex);
            AddDescriptorMessage("The creature descriptor catalog could not be loaded.");
            return;
        }

        if (groups.Count == 0)
        {
            AddDescriptorMessage($"No part catalog is available for {creatureId}.");
            AddDescriptorMessage("The saved descriptor values are preserved; use the JSON button to edit unsupported parts.");
            return;
        }

        var card = new StackPanel();
        var caption = string.IsNullOrWhiteSpace(pet.GetString("CustomName"))
            ? creatureId
            : $"{pet.GetString("CustomName")} · {creatureId}";
        card.Children.Add(new TextBlock
        {
            Text = caption,
            Style = PageStyle("Heading"),
            FontSize = 15,
            Margin = new Thickness(3, 1, 3, 7)
        });

        int descriptorIndex = 0;
        foreach (var group in groups)
            descriptorIndex = AddDescriptorGroup(card, group, descriptors, descriptorIndex, 0);

        if (descriptorIndex < descriptors.Length)
        {
            var extras = new StackPanel();
            extras.Children.Add(new TextBlock
            {
                Text = "Additional descriptor values",
                Style = PageStyle("Heading"),
                FontSize = 13,
                Margin = new Thickness(3, 7, 3, 5)
            });
            for (int i = descriptorIndex; i < descriptors.Length; i++)
                if (descriptors.Get(i) is { } value)
                    AddField(extras, descriptors, i, $"Part {i + 1}", value);
            card.Children.Add(extras);
        }

        _form.Children.Add(new Border
        {
            Style = PageStyle("Card"),
            Padding = new Thickness(9),
            Child = card
        });
        var actions = new WrapPanel { Margin = new Thickness(3, 5, 3, 2) };
        var regenerate = new Button { Content = "Regen Descriptor ID" };
        regenerate.Click += (_, _) => RegenerateDescriptorId(descriptors);
        actions.Children.Add(regenerate);
        _form.Children.Add(actions);
        _form.Children.Add(new TextBlock
        {
            Text = $"{descriptors.Length:N0} saved parts · Changes apply in memory. Press Save to keep them.",
            Margin = new Thickness(7, 7, 4, 3),
            TextWrapping = TextWrapping.Wrap
        });
    }

    private int AddDescriptorGroup(Panel panel, DescriptorGroup group, JsonArray descriptors, int index, int depth)
    {
        string? original = index < descriptors.Length ? descriptors.GetString(index) : null;
        string normalized = (original ?? string.Empty).TrimStart('^');
        var choices = group.Options.Select(option => new DescriptorChoice(option.Id, option.Name, option.Children)).ToList();
        var selected = choices.FirstOrDefault(option => string.Equals(option.Id, normalized, StringComparison.OrdinalIgnoreCase));
        if (selected is null && !string.IsNullOrEmpty(original))
        {
            selected = new DescriptorChoice(normalized, original, []);
            choices.Add(selected);
        }

        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170 + Math.Min(depth, 3) * 18) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new TextBlock
        {
            Text = group.Id.Trim('_').Replace('_', ' '),
            Style = PageStyle("Label"),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        });

        var picker = new ComboBox
        {
            ItemsSource = choices,
            DisplayMemberPath = nameof(DescriptorChoice.Name),
            SelectedItem = selected,
            MinWidth = 180,
            IsEnabled = index < descriptors.Length
        };
        System.Windows.Automation.AutomationProperties.SetName(picker, group.Id);
        Grid.SetColumn(picker, 1);
        row.Children.Add(picker);
        panel.Children.Add(row);
        _descriptorPickers.Add(picker);

        if (index < descriptors.Length)
        {
            int descriptorSlot = index;
            string baseline = original ?? string.Empty;
            picker.SelectionChanged += (_, _) =>
            {
                if (picker.SelectedItem is not DescriptorChoice choice ||
                    string.Equals(choice.Id, baseline.TrimStart('^'), StringComparison.OrdinalIgnoreCase))
                    return;
                if (!Commit())
                {
                    picker.SelectedItem = selected;
                    return;
                }

                string replacement = baseline.StartsWith('^') ? "^" + choice.Id : choice.Id;
                if (_entry is { } entry)
                {
                    _session.ApplyFields([new FieldEdit(descriptors, descriptorSlot, replacement, baseline)]);
                    _owner.PageStatus("Descriptor part updated in memory. Press Save to keep the changes.");
                    Select(entry);
                }
            };
        }

        index++;
        if (selected is not null)
            foreach (var child in selected.Children)
                index = AddDescriptorGroup(panel, child, descriptors, index, depth + 1);
        return index;
    }

    private List<DescriptorGroup> LoadDescriptorGroups(string creatureId)
    {
        if (string.IsNullOrWhiteSpace(creatureId)) return [];
        string file = Path.Combine(_session.ResourceRoot, "json", "Creature Descriptors.json");
        using var document = JsonDocument.Parse(File.ReadAllText(file));
        foreach (var creature in document.RootElement.EnumerateArray())
        {
            if (!creature.TryGetProperty("CreatureId", out var id) ||
                !string.Equals(id.GetString(), creatureId, StringComparison.OrdinalIgnoreCase))
                continue;
            return creature.TryGetProperty("Groups", out var groups) ? ReadDescriptorGroups(groups) : [];
        }
        return [];
    }

    private static List<DescriptorGroup> ReadDescriptorGroups(JsonElement groups)
    {
        var result = new List<DescriptorGroup>();
        foreach (var group in groups.EnumerateArray())
        {
            string id = group.TryGetProperty("GroupId", out var groupId) ? groupId.GetString() ?? "Part" : "Part";
            var options = new List<DescriptorChoice>();
            if (group.TryGetProperty("Options", out var optionArray))
            {
                foreach (var option in optionArray.EnumerateArray())
                {
                    string optionId = option.TryGetProperty("Id", out var idValue) ? idValue.GetString() ?? string.Empty : string.Empty;
                    if (string.IsNullOrWhiteSpace(optionId)) continue;
                    string name = option.TryGetProperty("Name", out var nameValue) ? nameValue.GetString() ?? optionId : optionId;
                    var children = option.TryGetProperty("Children", out var childArray) ? ReadDescriptorGroups(childArray) : [];
                    options.Add(new DescriptorChoice(optionId, name, children));
                }
            }
            result.Add(new DescriptorGroup(id, options));
        }
        return result;
    }

    private void RegenerateDescriptorId(JsonArray descriptors)
    {
        if (!Commit()) return;
        try
        {
            var parts = _descriptorPickers
                .Select(picker => picker.SelectedItem as DescriptorChoice)
                .Where(choice => choice is not null)
                .Select(choice => "^" + choice!.Id)
                .ToList();
            string id = RandomNumberGenerator.GetInt32(1, 10).ToString(System.Globalization.CultureInfo.InvariantCulture) +
                string.Concat(Enumerable.Range(0, 9).Select(_ => RandomNumberGenerator.GetInt32(0, 10).ToString(System.Globalization.CultureInfo.InvariantCulture)));
            parts.Add("^" + id);
            for (int i = descriptors.Length - 1; i >= 0; i--)
                descriptors.RemoveAt(i);
            foreach (string part in parts)
                descriptors.Add(part);

            if (_entry is { } entry) Select(entry);
            _owner.PageStatus("Descriptor ID regenerated in memory. Press Save to keep the changes.");
        }
        catch (Exception ex) { _owner.PageError(ex); }
    }

    private void AddDescriptorMessage(string text) =>
        _form.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(7, 8, 7, 8)
        });
}
