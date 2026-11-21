using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Globalization;
using System.IO;
using NMSE.Core;
using NMSE.Data;
using NMSE.Models;
using NMSE.Next.Engine;

namespace NMSE.Next.Desktop;

internal sealed partial class DomainPageView
{
    private sealed record StationStatDraft(JsonObject Entry, string StatId, TextBox Input, string Baseline);
    private readonly List<StationStatDraft> _visitedStationDrafts = [];
    internal bool VisitedStationDraftPending => _visitedStationDrafts.Any(d => d.Input.Text != d.Baseline);

    internal bool ValidateVisitedStationDrafts()
    {
        foreach (var draft in _visitedStationDrafts)
        {
            if (draft.Input.Text == draft.Baseline) continue;
            if (!int.TryParse(draft.Input.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value < 0)
            {
                _owner.PageError(new ArgumentException($"{draft.Input.Tag} must be a whole, non-negative number."));
                return false;
            }
        }
        return true;
    }

    internal void ApplyVisitedStationDrafts()
    {
        bool changed = false;
        foreach (var draft in _visitedStationDrafts)
        {
            if (draft.Input.Text == draft.Baseline || !int.TryParse(draft.Input.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)) continue;
            SpaceStationLogic.SetStatValue(draft.Entry, draft.StatId, value);
            changed = true;
        }
        if (changed) _session.MarkDirty();
        for (int i = 0; i < _visitedStationDrafts.Count; i++)
        {
            var draft = _visitedStationDrafts[i];
            _visitedStationDrafts[i] = draft with { Baseline = draft.Input.Text };
        }
    }

    private sealed record VisitedStationItem(NMSE.Core.SpaceStationLogic.StationSystemEntry System, string Title)
    {
        public override string ToString() => Title;
    }

    private FrameworkElement BuildVisitedStationsPage()
    {
        var player = _session.Player;
        if (player is null)
            return new TextBlock { Text = "Load a save to view visited stations.", Margin = new Thickness(12) };

        var systems = NMSE.Core.SpaceStationLogic.FindStationSystems(player);
        var items = systems.Select(system =>
        {
            string? baseName = NMSE.Core.SpaceStationLogic.ResolveStationBaseName(player, system.Address);
            string? discoveryName = baseName is null
                ? NMSE.Core.SpaceStationLogic.ResolveSystemName(_session.Data, system.Address)
                : null;
            string portalCode = NMSE.Core.SpaceStationLogic.AddressToPortalCode(system.Address);
            string title = baseName ?? discoveryName ?? $"Portal Code: {portalCode}";
            system.DisplayName = title;
            return new VisitedStationItem(system, title);
        }).OrderBy(item => item.Title.StartsWith("Portal Code:", StringComparison.Ordinal) ? 1 : 0)
          .ThenBy(item => item.System.StatsIndex)
          .ToArray();

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new Grid();
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        left.Children.Add(new TextBlock
        {
            Text = "Visited Stations",
            Style = PageStyle("Heading"),
            Margin = new Thickness(4, 2, 4, 8)
        });
        var count = new TextBlock
        {
            Text = UiStrings.Format("ui.visited.system_count", items.Length.ToString("N0", CultureInfo.CurrentCulture)),
            Margin = new Thickness(5, 0, 5, 7)
        };
        Grid.SetRow(count, 1);
        left.Children.Add(count);

        var list = new ListBox
        {
            ItemsSource = items,
            DisplayMemberPath = nameof(VisitedStationItem.Title),
            MinWidth = 210
        };
        list.SetResourceReference(Control.BackgroundProperty, "Input");
        list.SetResourceReference(Control.ForegroundProperty, "Text");
        list.SetResourceReference(Control.BorderBrushProperty, "Edge");
        Grid.SetRow(list, 2);
        left.Children.Add(list);
        var leftCard = new Border { Style = PageStyle("Card"), Padding = new Thickness(8), Margin = new Thickness(2, 2, 8, 2), Child = left };
        layout.Children.Add(leftCard);

        var details = new StackPanel();
        var detailsScroll = new ScrollViewer
        {
            Content = details,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var rightCard = new Border { Style = PageStyle("Card"), Padding = new Thickness(9), Child = detailsScroll };
        Grid.SetColumn(rightCard, 1);
        layout.Children.Add(rightCard);

        void ShowStation(VisitedStationItem item)
        {
            if (!Commit())
            {
                list.SelectedItem = item;
                return;
            }

            _fields.Clear();
            _visitedStationDrafts.Clear();
            details.Children.Clear();
            var system = item.System;
            _currentJsonSegments = _owner.RawPathForSection(_section).Append($"[{system.StatsIndex}]").ToArray();

            var header = new StackPanel { Margin = new Thickness(2, 0, 2, 8) };
            header.Children.Add(new TextBlock { Text = item.Title, Style = PageStyle("Heading"), FontSize = 18 });
            header.Children.Add(new TextBlock { Text = $"Address: {SaveSession.FieldText(system.Address)}", Margin = new Thickness(2, 3, 2, 0) });
            string portalCode = SpaceStationLogic.AddressToPortalCode(system.Address);
            header.Children.Add(new TextBlock { Text = $"Portal Code: {portalCode}", Margin = new Thickness(2, 2, 2, 0) });
            var glyphs = new WrapPanel { Margin = new Thickness(2, 5, 2, 2) };
            foreach (char glyph in portalCode) glyphs.Children.Add(_owner.PlayerGlyph(glyph));
            header.Children.Add(glyphs);
            details.Children.Add(header);

            var stats = new StackPanel();
            var stationCard = new Border
            {
                Style = PageStyle("Card"),
                Padding = new Thickness(9),
                Margin = new Thickness(0, 0, 0, 8),
                Child = stats
            };
            stats.Children.Add(new TextBlock { Text = "Space Station Stats", Style = PageStyle("Heading"), FontSize = 16, Margin = new Thickness(2, 0, 2, 6) });
            var statNames = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [NMSE.Core.SpaceStationLogic.StatWGuildStand] = "Mercenary Guild Standing",
                [NMSE.Core.SpaceStationLogic.StatEGuildStand] = "Merchant Guild Standing",
                [NMSE.Core.SpaceStationLogic.StatTGuildStand] = "Trade Guild Standing",
                [NMSE.Core.SpaceStationLogic.StatWarStanding] = "Vy'keen Standing",
                [NMSE.Core.SpaceStationLogic.StatTraStanding] = "Gek Standing",
                [NMSE.Core.SpaceStationLogic.StatExpStanding] = "Korvax Standing",
                [NMSE.Core.SpaceStationLogic.StatSpPoiMissions] = "Space POI Missions"
            };
            foreach (string statId in NMSE.Core.SpaceStationLogic.DisplayStatOrder)
            {
                string label = statNames.GetValueOrDefault(statId, statId);
                var stat = SpaceStationLogic.FindStat(system.Entry, statId);
                var value = stat?.GetObject("Value");
                if (value?.Get("IntValue") is { } intValue)
                {
                    AddField(stats, value, "IntValue", label, intValue);
                    continue;
                }

                var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(new TextBlock { Text = label, Style = PageStyle("Label"), VerticalAlignment = VerticalAlignment.Center });
                var input = new TextBox { Text = SpaceStationLogic.GetStatValue(system.Entry, statId).ToString(CultureInfo.InvariantCulture), Tag = label, MinWidth = 90 };
                Grid.SetColumn(input, 1);
                row.Children.Add(input);
                stats.Children.Add(row);
                _visitedStationDrafts.Add(new StationStatDraft(system.Entry, statId, input, input.Text));
            }

            var actions = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            var claimable = new Button { Content = "Set Claimable Values", Padding = new Thickness(10, 5, 10, 5) };
            claimable.Click += (_, _) =>
            {
                if (!Commit()) return;
                var changes = new List<FieldEdit>();
                foreach (string statId in NMSE.Core.SpaceStationLogic.StationStatIds)
                {
                    var stat = NMSE.Core.SpaceStationLogic.FindStat(system.Entry, statId);
                    var value = stat?.GetObject("Value");
                    if (value is null || value.Get("IntValue") is not { } current) continue;
                    string baseline = SaveSession.FieldText(current);
                    int target = NMSE.Core.SpaceStationLogic.GetClaimableTarget(statId);
                    if (int.TryParse(baseline, out int parsed) && parsed < target)
                        changes.Add(new FieldEdit(value, "IntValue", target.ToString(System.Globalization.CultureInfo.InvariantCulture), baseline));
                }
                if (changes.Count > 0) _session.ApplyFields(changes);
                if (SpaceStationLogic.ApplyClaimableMinimums(system.Entry)) _session.MarkDirty();
                _owner.PageStatus("Station values updated in memory. Press Save to keep them.");
                ShowStation(item);
            };
            actions.Children.Add(claimable);
            var json = _owner.JsonRedirectButton("↗ JSON", "Open this visited station's stats in Raw JSON Editor", () => _owner.NavigateToRawJsonPath(_currentJsonSegments));
            actions.Children.Add(json);
            stats.Children.Add(actions);
            details.Children.Add(stationCard);

            var poi = NMSE.Core.SpaceStationLogic.FindSpacePoiEntry(player.GetArray("SpacePoiDiscoveries"), system.Address);
            var poiCardBody = new StackPanel();
            poiCardBody.Children.Add(new TextBlock { Text = "Space POI Discoveries", Style = PageStyle("Heading"), FontSize = 16, Margin = new Thickness(2, 0, 2, 6) });
            if (poi is { } poiEntry)
            {
                if (SpacePoiTableDatabase.Types.Count == 0)
                    SpacePoiTableDatabase.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "Resources", "json", "Space POI.json"));
                foreach (string fieldName in new[] { "PackedData0", "PackedData1" })
                {
                    object? raw = poiEntry.Data.Get(fieldName);
                    if (raw is not null)
                        AddField(poiCardBody, poiEntry.Data, fieldName, fieldName, raw);
                }
                var poiPath = _owner.RawPathForSection(_section)
                    .Take(_owner.RawPathForSection(_section).Length - 1)
                    .Append("SpacePoiDiscoveries")
                    .Append($"[{poiEntry.Index}]")
                    .ToArray();
                poiCardBody.Children.Add(_owner.JsonRedirectButton("↗ JSON · Space POIs", "Open this system's Space POI data in Raw JSON Editor", () => _owner.NavigateToRawJsonPath(poiPath)));
                BuildSpacePoiSlotControls(poiEntry.Data, poiCardBody);
            }
            else
            {
                poiCardBody.Children.Add(new TextBlock { Text = "This visited system has no saved Space POI discovery entry.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(3) });
            }
            details.Children.Add(new Border { Style = PageStyle("Card"), Padding = new Thickness(9), Margin = new Thickness(0, 0, 0, 8), Child = poiCardBody });

            var missions = new StackPanel();
            missions.Children.Add(new TextBlock { Text = "Station Missions", Style = PageStyle("Heading"), FontSize = 16, Margin = new Thickness(2, 0, 2, 6) });
            foreach (var (id, label) in new[]
            {
                ("^CLAIM_STAT_TUT", "Claim Station Tutorial"),
                ("^SPACEBASE_TUT", "Space Base Tutorial"),
                ("^STATIONOWN_WIKI", "Station Ownership Wiki")
            })
            {
                var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(new TextBlock { Text = label, Style = PageStyle("Label"), VerticalAlignment = VerticalAlignment.Center });
                var selector = new ComboBox
                {
                    ItemsSource = new[] { UiStrings.Get("base.station.mission_not_started"), UiStrings.Get("base.station.mission_active"), UiStrings.Get("base.station.mission_completed") },
                    SelectedIndex = (int)NMSE.Core.SpaceStationLogic.GetMissionState(player, id),
                    MinWidth = 160
                };
                selector.SelectionChanged += (_, _) =>
                {
                    if (selector.SelectedIndex < 0) return;
                    var state = (NMSE.Core.SpaceStationLogic.MissionState)selector.SelectedIndex;
                    NMSE.Core.SpaceStationLogic.ApplyMissionState(player, id, state);
                    _owner.PageStatus("Station mission changes applied in memory. Press Save to keep them.");
                };
                Grid.SetColumn(selector, 1);
                row.Children.Add(selector);
                missions.Children.Add(row);
            }
            details.Children.Add(new Border { Style = PageStyle("Card"), Padding = new Thickness(9), Child = missions });
        }

        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is VisitedStationItem item)
                ShowStation(item);
        };

        if (items.Length == 0)
        {
            details.Children.Add(new TextBlock
            {
                Text = "No visited station systems were found in this save.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(8)
            });
        }
        else
        {
            list.SelectedIndex = 0;
        }

        return layout;
    }

    private void BuildSpacePoiSlotControls(JsonObject data, StackPanel parent)
    {
        if (!SpaceStationLogic.TryGetPackedData(data, "PackedData0", out ulong packed0, out bool hex0)
            || !SpaceStationLogic.TryGetPackedData(data, "PackedData1", out ulong packed1, out bool hex1))
        {
            parent.Children.Add(new TextBlock { Text = UiStrings.Get("ui.visited.packed_required"), Margin = new Thickness(3, 8, 3, 3), TextWrapping = TextWrapping.Wrap });
            return;
        }

        var rules = SpacePoiTableDatabase.Types;
        var inferred = SpacePoiSlotInference.Infer(packed0, packed1, rules);
        string[] tokens = Enumerable.Range(0, SpaceStationLogic.PackedDataSlotCount)
            .Select(index =>
            {
                if (inferred is null) return SpacePoiSlotTokens.Unknown;
                var slot = inferred.Slots[index];
                if (slot.TypeIsUnique && slot.TypeIndex >= 0 && slot.TypeIndex < rules.Count)
                    return rules[slot.TypeIndex].Type;
                return slot.CoveredByLayouts ? SpacePoiSlotTokens.Unknown : SpacePoiSlotTokens.Unallocated;
            }).ToArray();

        var hint = new TextBlock
        {
            Text = inferred is null
                ? UiStrings.Get("ui.visited.hint_infer")
                : UiStrings.Get("ui.visited.hint_slots"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(3, 8, 3, 6)
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Secondary");
        parent.Children.Add(hint);

        string[] levels = [UiStrings.Get("base.station.packed_data_level_hidden"), UiStrings.Get("base.station.packed_data_level_undiscovered"), UiStrings.Get("base.station.packed_data_level_discovered"), UiStrings.Get("base.station.packed_data_level_completed")];
        var grouped = new List<(string Token, string Label, List<int> Slots)>();
        foreach (string token in tokens.Distinct(StringComparer.Ordinal))
        {
            string label = token switch
            {
                SpacePoiSlotTokens.Unknown => UiStrings.Get("ui.visited.unknown_poi"),
                SpacePoiSlotTokens.Unallocated => UiStrings.Get("ui.visited.unallocated"),
                _ => rules.FirstOrDefault(rule => rule.Type == token)?.DisplayName ?? token
            };
            var group = grouped.FirstOrDefault(item => item.Token == token);
            if (group.Slots is null)
            {
                group = (token, label, []);
                grouped.Add(group);
            }
            group.Slots.AddRange(Enumerable.Range(0, tokens.Length).Where(index => tokens[index] == token && !group.Slots.Contains(index)));
        }

        foreach (var group in grouped)
        {
            var card = new StackPanel();
            card.Children.Add(new TextBlock
            {
                Text = $"{group.Label} (slot{(group.Slots.Count == 1 ? "" : "s")} {string.Join(", ", group.Slots.Select(index => index + 1))})",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(2, 4, 2, 3)
            });
            var slotsGrid = new UniformGrid { Columns = 4 };
            foreach (int slotIndex in group.Slots)
            {
                bool firstPacked = slotIndex < 16;
                int localSlot = firstPacked ? slotIndex : slotIndex - 16;
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 1, 8, 3), VerticalAlignment = VerticalAlignment.Center };
                row.Children.Add(new TextBlock { Text = $"{slotIndex + 1}", Width = 26, VerticalAlignment = VerticalAlignment.Center });
                var selector = new ComboBox { ItemsSource = levels, SelectedIndex = (int)SpaceStationLogic.GetPackedDataSlot(firstPacked ? packed0 : packed1, localSlot), MinWidth = 125 };
                selector.SelectionChanged += (_, _) =>
                {
                    if (selector.SelectedIndex < 0 || !Commit()) return;
                    var level = (SpaceStationLogic.SpacePoiDiscoveryLevel)selector.SelectedIndex;
                    ulong before = firstPacked ? packed0 : packed1;
                    ulong after = SpaceStationLogic.SetPackedDataSlot(before, localSlot, level);
                    if (after == before) return;
                    if (!SpaceStationLogic.SetPackedData(data, firstPacked ? "PackedData0" : "PackedData1", after, firstPacked ? hex0 : hex1)) return;
                    if (firstPacked) packed0 = after; else packed1 = after;
                    _session.MarkDirty();
                    _owner.PageStatus("Space POI discovery changes applied in memory. Press Save to keep them.");
                };
                row.Children.Add(selector);
                slotsGrid.Children.Add(row);
            }
            card.Children.Add(slotsGrid);
            parent.Children.Add(new Border { Style = PageStyle("Card"), Padding = new Thickness(7), Margin = new Thickness(0, 3, 0, 3), Child = card });
        }
    }
}
