using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NMSE.Core;
using NMSE.Core.Utilities;
using NMSE.Data;
using NMSE.Models;
using NMSE.Next.Engine;

namespace NMSE.Next.Desktop;

internal sealed partial class DomainPageView
{
    sealed record FleetRecordChoice(int Index, string Label)
    {
        public override string ToString() => Label;
    }
    sealed record FrigateTypeChoice(string Value, string Label)
    {
        public override string ToString() => Label;
    }
    sealed record RaceChoice(string Value, string Label)
    {
        public override string ToString() => Label;
    }

    UIElement BuildFrigatesPage()
    {
        if (_session.Player is not { } player) return new TextBlock { Text = "Frigate data is unavailable." };
        var frigates = player.GetArray("FleetFrigates");
        if (FrigateTraitDatabase.Traits.Count == 0)
            FrigateTraitDatabase.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "Resources", "json", "Frigate Traits.json"));
        if (frigates is null) return new TextBlock { Text = "No frigate records are present in this save." };
        var expeditions = player.GetArray("FleetExpeditions");
        var layout = new Grid { MinWidth = 820 };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new DockPanel();
        var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 7) };
        var listJson = _owner.JsonRedirectButton("↗ JSON · Frigates", "Open the frigate list in Raw JSON Editor", () => _owner.NavigateToRawJsonPath("PlayerStateData", "FleetFrigates"));
        toolbar.Children.Add(listJson);
        toolbar.Children.Add(_owner.JsonRedirectButton("↗ JSON · Expeditions", "Open fleet expeditions in Raw JSON Editor", () => _owner.NavigateToRawJsonPath("PlayerStateData", "FleetExpeditions")));
        var list = new ListBox { MinHeight = 260, MaxHeight = 720 };
        list.SetResourceReference(Control.BackgroundProperty, "Input");
        list.SetResourceReference(Control.ForegroundProperty, "Text");
        list.SetResourceReference(Control.BorderBrushProperty, "Edge");
        var count = new TextBlock { Margin = new Thickness(4, 6, 4, 8) };
        var listArea = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top); listArea.Children.Add(toolbar);
        DockPanel.SetDock(count, Dock.Bottom); listArea.Children.Add(count);
        listArea.Children.Add(list);
        var listActions = new WrapPanel { Margin = new Thickness(0, 7, 0, 0) };
        Button Action(string label, Action click, bool primary = false)
        {
            var button = new Button { Content = label, Margin = new Thickness(2) };
            if (primary) button.Style = PageStyle("Primary");
            button.Click += (_, _) => click();
            listActions.Children.Add(button);
            return button;
        }
        var details = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        void UpdateList()
        {
            var choices = Enumerable.Range(0, frigates.Length).Select(i =>
            {
                var f = frigates.GetObject(i);
                return new FleetRecordChoice(i, f is null ? $"Frigate {i + 1}" : SectionCatalog.FrigateCaption(f, i));
            }).ToArray();
            int selected = (list.SelectedItem as FleetRecordChoice)?.Index ?? 0;
            list.ItemsSource = choices;
            list.DisplayMemberPath = nameof(FleetRecordChoice.Label);
            count.Text = $"{frigates.Length} / 30 frigates";
            list.SelectedItem = choices.FirstOrDefault(x => x.Index == selected) ?? choices.FirstOrDefault();
        }
        void ShowSelected()
        {
            int index = (list.SelectedItem as FleetRecordChoice)?.Index ?? -1;
            if (index < 0 || index >= frigates.Length) { details.Content = new TextBlock { Text = "Select a frigate to edit." }; return; }
            var f = frigates.GetObject(index);
            if (f is null) { details.Content = new TextBlock { Text = "Frigate data is unavailable." }; return; }
            var root = new StackPanel();
            var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 7) };
            var raw = _owner.JsonRedirectButton("↗ JSON · Selected", "Open this frigate in Raw JSON Editor", () => _owner.NavigateToRawJsonPath("PlayerStateData", "FleetFrigates", $"[{index}]"));
            DockPanel.SetDock(raw, Dock.Right); heading.Children.Add(raw);
            heading.Children.Add(new TextBlock { Text = SectionCatalog.FrigateCaption(f, index), Style = PageStyle("Heading"), FontSize = 18 });
            root.Children.Add(heading);
            var columns = new Grid();
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var info = new StackPanel();
            var stats = new StackPanel();
            void SetText(string key, string value) { f.Set(key, value); _session.MarkDirty(); UpdateList(); }
            void SetInt(string key, string text)
            {
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < 0)
                    throw new ArgumentException($"{Readable(key)} must be a non-negative whole number.");
                f.Set(key, n); _session.MarkDirty();
            }
            void Row(Panel host, string label, FrameworkElement control)
            {
                var row = new Grid { Margin = new Thickness(2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(145) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(new TextBlock { Text = label, Style = PageStyle("Label"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
                Grid.SetColumn(control, 1); row.Children.Add(control); host.Children.Add(row);
            }
            TextBox TextField(string key, string value)
            {
                var box = new TextBox { Text = value ?? "" };
                box.LostFocus += (_, _) => { try { SetText(key, box.Text); } catch (Exception ex) { _owner.PageError(ex); } };
                return box;
            }
            TextBox NumberField(string key, int value)
            {
                var box = new TextBox { Text = value.ToString(CultureInfo.InvariantCulture) };
                box.LostFocus += (_, _) => { try { SetInt(key, box.Text); } catch (Exception ex) { _owner.PageError(ex); } };
                return box;
            }
            string ReadSeed(string key)
            {
                var a = f.GetArray(key); return a is { Length: > 1 } ? a.Get(1)?.ToString() ?? "" : "";
            }
            void SeedField(string label, string key)
            {
                var a = f.GetArray(key);
                var box = new TextBox { Text = ReadSeed(key) };
                var row = new Grid { Margin = new Thickness(2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(145) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new TextBlock { Text = label, Style = PageStyle("Label"), VerticalAlignment = VerticalAlignment.Center });
                Grid.SetColumn(box, 1); row.Children.Add(box);
                var gen = new Button { Content = "Generate", Margin = new Thickness(4, 0, 0, 0) };
                gen.Click += (_, _) => box.Text = "0x" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
                Grid.SetColumn(gen, 2); row.Children.Add(gen);
                box.LostFocus += (_, _) =>
                {
                    string? normalized = SeedHelper.NormalizeSeed(box.Text);
                    if (normalized is null) { _owner.PageError(new ArgumentException($"{label} must be a valid hexadecimal seed.")); return; }
                    if (a is { Length: > 1 }) { a.Set(1, normalized); _session.MarkDirty(); }
                };
                info.Children.Add(row);
            }

            info.Children.Add(new TextBlock { Text = "Frigate Info", Style = PageStyle("Heading"), FontSize = 16, Margin = new Thickness(3, 3, 3, 7) });
            Row(info, "Name", TextField("CustomName", f.GetString("CustomName") ?? ""));
            string oldType = FrigateLogic.GetFrigateType(f);
            var typeChoices = FrigateLogic.FrigateTypes.Select(value => new FrigateTypeChoice(value, FrigateLogic.GetLocalisedFrigateTypeName(value))).ToArray();
            var type = new ComboBox { ItemsSource = typeChoices, SelectedItem = typeChoices.FirstOrDefault(choice => choice.Value == oldType) };
            type.SelectionChanged += (_, _) =>
            {
                if (type.SelectedItem is not FrigateTypeChoice choice || choice.Value == oldType) return;
                string next = choice.Value;
                FrigateLogic.AutoAdjustTraitsForTypeChange(f, oldType, next);
                f.GetObject("FrigateClass")?.Set("FrigateClass", next);
                _session.MarkDirty(); UpdateList(); ShowSelected();
            };
            Row(info, "Type", type);
            string grade = FrigateLogic.ComputeClassFromTraits(f);
            var cls = new ComboBox { ItemsSource = FrigateLogic.FrigateGrades, SelectedItem = grade };
            cls.SelectionChanged += (_, _) =>
            {
                if (cls.SelectedItem is not string next || next == FrigateLogic.ComputeClassFromTraits(f)) return;
                FrigateLogic.AdjustTraitsForTargetGrade(f, next);
                f.GetObject("InventoryClass")?.Set("InventoryClass", FrigateLogic.ComputeClassFromTraits(f));
                _session.MarkDirty(); UpdateList(); ShowSelected();
            };
            Row(info, "Class", cls);
            var raceArray = f.GetObject("Race");
            var raceChoices = FrigateLogic.FrigateRaces.Select(value => new RaceChoice(value, FrigateLogic.GetLocalisedFrigateRaceName(value))).ToArray();
            var race = new ComboBox { ItemsSource = raceChoices, SelectedItem = raceChoices.FirstOrDefault(choice => choice.Value == raceArray?.GetString("AlienRace")) };
            race.SelectionChanged += (_, _) =>
            {
                if (raceArray is null || race.SelectedItem is not RaceChoice value) return;
                raceArray.Set("AlienRace", value.Value); _session.MarkDirty();
            };
            Row(info, "NPC Race", race);
            SeedField("Home Seed", "HomeSystemSeed");
            SeedField("Model Seed", "ResourceSeed");
            info.Children.Add(new TextBlock { Text = "Traits", Style = PageStyle("Heading"), FontSize = 15, Margin = new Thickness(3, 9, 3, 4) });
            var traitArray = f.GetArray("TraitIDs");
            var traitChoices = new[] { FrigateTraitDatabase.None }.Concat(FrigateTraitDatabase.Traits).ToArray();
            for (int traitIndex = 0; traitIndex < 5; traitIndex++)
            {
                int ti = traitIndex;
                string id = traitArray is not null && ti < traitArray.Length ? traitArray.GetString(ti) : "^";
                var combo = new ComboBox { ItemsSource = traitChoices, DisplayMemberPath = nameof(FrigateTrait.DisplayName), SelectedItem = traitChoices.FirstOrDefault(t => t.Id == id) ?? FrigateTraitDatabase.None };
                combo.SelectionChanged += (_, _) =>
                {
                    if (traitArray is null || ti >= traitArray.Length || combo.SelectedItem is not FrigateTrait selectedTrait) return;
                    traitArray.Set(ti, selectedTrait.Id); string updated = FrigateLogic.ComputeClassFromTraits(f);
                    f.GetObject("InventoryClass")?.Set("InventoryClass", updated); _session.MarkDirty(); UpdateList(); ShowSelected();
                };
                Row(info, $"Trait {traitIndex + 1}", combo);
            }
            int damage = f.GetInt("DamageTaken");
            var damageText = new TextBlock { Text = damage > 0 ? $"Damage: {damage}" : "No damage", VerticalAlignment = VerticalAlignment.Center };
            var repair = new Button { Content = "Repair", IsEnabled = damage > 0, Margin = new Thickness(3) };
            repair.Click += (_, _) => { f.Set("DamageTaken", 0); f.Set("RepairsMade", 0); _session.MarkDirty(); ShowSelected(); };
            var damageRow = new WrapPanel(); damageRow.Children.Add(damageText); damageRow.Children.Add(repair); info.Children.Add(damageRow);

            stats.Children.Add(new TextBlock { Text = "Stats", Style = PageStyle("Heading"), FontSize = 16, Margin = new Thickness(3, 3, 3, 7) });
            var statArray = f.GetArray("Stats");
            for (int i = 0; i < FrigateLogic.StatNames.Length; i++)
            {
                int si = i; int value = statArray is not null && si < statArray.Length ? statArray.GetInt(si) : 0;
                var box = new TextBox { Text = value.ToString(CultureInfo.InvariantCulture) };
                box.LostFocus += (_, _) =>
                {
                    if (statArray is null || si >= statArray.Length) return;
                    if (!int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < 0)
                    {
                        _owner.PageError(new ArgumentException($"{FrigateLogic.StatNames[si]} must be a non-negative whole number."));
                        return;
                    }
                    statArray.Set(si, n); _session.MarkDirty();
                };
                Row(stats, FrigateLogic.StatNames[i], box);
            }
            stats.Children.Add(new TextBlock { Text = "Totals", Style = PageStyle("Heading"), FontSize = 15, Margin = new Thickness(3, 9, 3, 4) });
            foreach (var item in new[] { ("Expeditions", "TotalNumberOfExpeditions"), ("Successful", "TotalNumberOfSuccessfulEvents"), ("Failed", "TotalNumberOfFailedEvents"), ("Times Damaged", "NumberOfTimesDamaged") })
                Row(stats, item.Item1, NumberField(item.Item2, f.GetInt(item.Item2)));
            int total = f.GetInt("TotalNumberOfExpeditions");
            int next = FrigateLogic.GetLevelUpIn(total);
            stats.Children.Add(new TextBlock { Text = "Progress / Mission", Style = PageStyle("Heading"), FontSize = 15, Margin = new Thickness(3, 9, 3, 4) });
            Row(stats, "State", new TextBox { Text = FrigateLogic.FrigateStates[FrigateLogic.GetFrigateState(f, index, expeditions)], IsReadOnly = true });
            Row(stats, "Level Up In", new TextBox { Text = next < 0 ? "Max" : next.ToString(CultureInfo.InvariantCulture), IsReadOnly = true });
            Row(stats, "Levels Left", new TextBox { Text = FrigateLogic.GetLevelUpsRemaining(total).ToString(CultureInfo.InvariantCulture), IsReadOnly = true });
            var ff = new Button { Content = "Fast Forward to Next Level" };
            ff.Click += (_, _) =>
            {
                int current = f.GetInt("TotalNumberOfExpeditions");
                int threshold = FrigateLogic.LevelVictoriesRequired.FirstOrDefault(x => current < x);
                if (threshold > 0) { f.Set("TotalNumberOfExpeditions", threshold - 1); _session.MarkDirty(); ShowSelected(); }
            };
            stats.Children.Add(ff);
            int expeditionIndex = expeditions is null ? -1 : FrigateLogic.FindExpeditionIndex(index, expeditions);
            if (expeditionIndex >= 0 && expeditions is not null)
            {
                var expedition = expeditions.GetObject(expeditionIndex);
                Row(stats, "Mission Type", new TextBox { Text = FrigateLogic.GetExpeditionCategory(expeditions, expeditionIndex), IsReadOnly = true });
                DatePicker start = new() { SelectedDate = DateTimeOffset.FromUnixTimeSeconds(expedition.GetLong("StartTime")).LocalDateTime };
                start.SelectedDateChanged += (_, _) =>
                {
                    if (start.SelectedDate is DateTime dt) { expedition.Set("StartTime", new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Local)).ToUnixTimeSeconds()); _session.MarkDirty(); }
                };
                Row(stats, "Expedition Start", start);
                var finish = new Button { Content = "Finish Expedition", Style = PageStyle("Primary"), Margin = new Thickness(3) };
                finish.Click += (_, _) => { FinishFleetExpedition(expedition, frigates); _session.MarkDirty(); ShowSelected(); };
                stats.Children.Add(finish);
            }

            Grid.SetColumn(stats, 1); columns.Children.Add(info); columns.Children.Add(stats); root.Children.Add(new Border { Style = PageStyle("Card"), Padding = new Thickness(9), Child = columns });
            details.Content = root;
        }
        list.SelectionChanged += (_, _) => ShowSelected();
        void RefreshAfterAction(int indexToSelect)
        {
            list.ItemsSource = null; UpdateList();
            list.SelectedItem = list.Items.Cast<FleetRecordChoice>().FirstOrDefault(x => x.Index == indexToSelect);
            ShowSelected();
        }
        Action("Delete", () =>
        {
            int index = (list.SelectedItem as FleetRecordChoice)?.Index ?? -1;
            if (index < 0 || index >= frigates.Length) return;
            if (expeditions is not null && FrigateLogic.FindExpeditionIndex(index, expeditions) >= 0) { _owner.PageError(new InvalidOperationException("A frigate on an expedition cannot be deleted.")); return; }
            if (MessageBox.Show(_owner, UiStrings.LocalizeText("Delete the selected frigate?"), UiStrings.LocalizeText("Delete Frigate"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            frigates.RemoveAt(index);
            if (expeditions is not null) FrigateLogic.AdjustExpeditionIndicesAfterRemoval(index, expeditions);
            _session.MarkDirty(); UpdateList(); ShowSelected();
        });
        Action("Copy", () =>
        {
            int index = (list.SelectedItem as FleetRecordChoice)?.Index ?? -1;
            if (index < 0 || index >= frigates.Length) return;
            if (frigates.Length >= 30) { _owner.PageError(new InvalidOperationException("The fleet already contains the maximum of 30 frigates.")); return; }
            frigates.Add(frigates.GetObject(index).DeepClone()); _session.MarkDirty(); RefreshAfterAction(frigates.Length - 1);
        });
        Action("Export", () => ExportFleetRecord(frigates, (list.SelectedItem as FleetRecordChoice)?.Index ?? -1, "Frigate JSON", "frigate.json"));
        Action("Import", () =>
        {
            if (frigates.Length >= 30) { _owner.PageError(new InvalidOperationException("The fleet already contains the maximum of 30 frigates.")); return; }
            var dialog = new OpenFileDialog { Filter = UiStrings.LocalizeText("Frigate JSON|*.json|Frigate files|*.flt|All files|*.*") };
            if (dialog.ShowDialog(_owner) != true) return;
            try
            {
                var imported = JsonObject.ImportFromFile(dialog.FileName);
                if (imported.GetObject("Data") is JsonObject data && data.GetObject("Frigate") is JsonObject wrapped) imported = wrapped;
                frigates.Add(imported); _session.MarkDirty(); RefreshAfterAction(frigates.Length - 1);
            }
            catch (Exception ex) { _owner.PageError(ex); }
        });
        DockPanel.SetDock(listActions, Dock.Bottom);
        left.Children.Add(listActions);
        left.Children.Add(new Border { Style = PageStyle("Card"), Padding = new Thickness(8), Child = listArea });
        UpdateList();
        ShowSelected();
        Grid.SetColumn(details, 1); layout.Children.Add(left); layout.Children.Add(details);
        return layout;
    }

    UIElement BuildSquadronPage()
    {
        if (_session.Player is not { } player) return new TextBlock { Text = "Squadron data is unavailable." };
        var pilots = player.GetArray("SquadronPilots");
        if (pilots is null) return new TextBlock { Text = "No squadron pilot records are present in this save." };
        var unlocked = player.GetArray("SquadronUnlockedPilotSlots");
        var layout = new Grid { MinWidth = 820 };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new DockPanel();
        var toolbar = new WrapPanel();
        toolbar.Children.Add(_owner.JsonRedirectButton("↗ JSON · Squadron", "Open the squadron pilot list in Raw JSON Editor", () => _owner.NavigateToRawJsonPath("PlayerStateData", "SquadronPilots")));
        DockPanel.SetDock(toolbar, Dock.Top); left.Children.Add(toolbar);
        var count = new TextBlock { Margin = new Thickness(4, 6, 4, 8), Text = UiStrings.Format("squadron.total_pilots", pilots.Length) };
        DockPanel.SetDock(count, Dock.Bottom); left.Children.Add(count);
        var list = new ListBox { MinHeight = 260, MaxHeight = 720 };
        list.SetResourceReference(Control.BackgroundProperty, "Input");
        list.SetResourceReference(Control.ForegroundProperty, "Text");
        list.SetResourceReference(Control.BorderBrushProperty, "Edge");
        left.Children.Add(new Border { Style = PageStyle("Card"), Padding = new Thickness(8), Child = list });
        var actions = new WrapPanel { Margin = new Thickness(0, 7, 0, 0) };
        var details = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        string[] races = SquadronLogic.PilotRaces;
        var shipTypes = SquadronLogic.GetShipTypeItems();
        FleetRecordChoice? SelectedChoice() => list.SelectedItem as FleetRecordChoice;
        string PilotCaption(JsonObject pilot, int i)
        {
            var npcSeed = SquadronLogic.ReadSeed(pilot, "NPCResource");
            var shipSeed = SquadronLogic.ReadSeed(pilot, "ShipResource");
            if (npcSeed == "0x0" || shipSeed == "0x0") return UiStrings.Format("squadron.pilot_empty", i);
            return UiStrings.Format("squadron.pilot_caption", i, UiStrings.LocalizeText(SquadronLogic.GetPilotRace(pilot)), UiStrings.LocalizeText(SquadronLogic.GetShipType(pilot)), SquadronLogic.PilotRanks[Math.Clamp(pilot.GetInt("PilotRank"), 0, 3)]);
        }
        void UpdateList(int select = -1)
        {
            var choices = Enumerable.Range(0, pilots.Length).Select(i => new FleetRecordChoice(i, PilotCaption(pilots.GetObject(i), i))).ToArray();
            int index = select >= 0 ? select : SelectedChoice()?.Index ?? 0;
            list.ItemsSource = null; list.ItemsSource = choices; list.DisplayMemberPath = nameof(FleetRecordChoice.Label);
            list.SelectedItem = choices.FirstOrDefault(x => x.Index == index) ?? choices.FirstOrDefault();
            count.Text = UiStrings.Format("squadron.total_pilots", pilots.Length);
        }
        void ShowSelected()
        {
            int index = SelectedChoice()?.Index ?? -1;
            if (index < 0 || index >= pilots.Length) { details.Content = new TextBlock { Text = "Select a squadron pilot slot." }; return; }
            var pilot = pilots.GetObject(index);
            if (pilot is null) { details.Content = new TextBlock { Text = "Pilot data is unavailable." }; return; }
            var root = new StackPanel();
            var title = new DockPanel { Margin = new Thickness(0, 0, 0, 7) };
            title.Children.Add(new TextBlock { Text = UiStrings.Format("squadron.pilot_format", index + 1), Style = PageStyle("Heading"), FontSize = 18 });
            var json = _owner.JsonRedirectButton("↗ JSON · Selected", "Open this pilot in Raw JSON Editor", () => _owner.NavigateToRawJsonPath("PlayerStateData", "SquadronPilots", $"[{index}]"));
            DockPanel.SetDock(json, Dock.Right); title.Children.Insert(0, json); root.Children.Add(title);
            void Row(string label, FrameworkElement control)
            {
                var row = new Grid { Margin = new Thickness(2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(new TextBlock { Text = label, Style = PageStyle("Label"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
                Grid.SetColumn(control, 1); row.Children.Add(control); root.Children.Add(row);
            }
            void Changed() { _session.MarkDirty(); UpdateList(index); }
            var race = new ComboBox { ItemsSource = races, SelectedItem = races.FirstOrDefault(x => string.Equals(x, SquadronLogic.GetPilotRace(pilot), StringComparison.OrdinalIgnoreCase)) };
            race.SelectionChanged += (_, _) =>
            {
                if (race.SelectedItem is not string value) return;
                SquadronLogic.SetPilotRace(pilot, value);
                Changed();
            };
            Row(UiStrings.LocalizeText("Race"), race);
            string[] ranks = SquadronLogic.PilotRanks;
            var rank = new ComboBox { ItemsSource = ranks, SelectedIndex = Math.Clamp(pilot.GetInt("PilotRank"), 0, 3) };
            rank.SelectionChanged += (_, _) => { if (rank.SelectedIndex >= 0) { pilot.Set("PilotRank", rank.SelectedIndex); Changed(); } };
            Row(UiStrings.LocalizeText("Rank"), rank);
            var ship = new ComboBox { ItemsSource = shipTypes, DisplayMemberPath = nameof(StarshipLogic.ShipTypeItem.DisplayName), SelectedItem = shipTypes.FirstOrDefault(x => x.InternalName == SquadronLogic.GetShipType(pilot)) };
            ship.SelectionChanged += (_, _) =>
            {
                if (ship.SelectedItem is not StarshipLogic.ShipTypeItem choice) return;
                if (pilot.GetObject("ShipResource") is { } shipResource && SquadronLogic.ShipTypeToResource.TryGetValue(choice.InternalName, out var filename))
                {
                    shipResource.Set("Filename", filename);
                    Changed();
                }
            };
            Row(UiStrings.LocalizeText("Ship Type"), ship);

            void SeedRow(string label, string resourceKey)
            {
                var res = pilot.GetObject(resourceKey);
                var seedArray = res?.GetArray("Seed");
                var box = new TextBox { Text = SquadronLogic.ReadSeed(pilot, resourceKey) };
                var row = new Grid { Margin = new Thickness(2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new TextBlock { Text = label, Style = PageStyle("Label"), VerticalAlignment = VerticalAlignment.Center });
                Grid.SetColumn(box, 1); row.Children.Add(box);
                var gen = new Button { Content = "Generate", Margin = new Thickness(4, 0, 0, 0) };
                gen.Click += (_, _) => box.Text = "0x" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
                Grid.SetColumn(gen, 2); row.Children.Add(gen);
                box.LostFocus += (_, _) =>
                {
                    string? normalized = SeedHelper.NormalizeSeed(box.Text);
                    if (normalized is null) { _owner.PageError(new ArgumentException($"{label} must be a valid hexadecimal seed.")); return; }
                    if (seedArray is { Length: > 1 }) { seedArray.Set(1, normalized); Changed(); }
                };
                root.Children.Add(row);
            }
            SeedRow("NPC Seed", "NPCResource");
            SeedRow("Ship Seed", "ShipResource");
            var traitsSeed = new TextBox { Text = pilot.GetString("TraitsSeed") ?? "" };
            traitsSeed.LostFocus += (_, _) =>
            {
                string? normalized = SeedHelper.NormalizeSeed(traitsSeed.Text);
                if (normalized is null) { _owner.PageError(new ArgumentException("Traits Seed must be a valid hexadecimal seed.")); return; }
                pilot.Set("TraitsSeed", normalized); Changed();
            };
            var traitsRow = new Grid { Margin = new Thickness(2) };
            traitsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            traitsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            traitsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            traitsRow.Children.Add(new TextBlock { Text = "Traits Seed", Style = PageStyle("Label"), VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(traitsSeed, 1); traitsRow.Children.Add(traitsSeed);
            var genTraits = new Button { Content = "Generate", Margin = new Thickness(4, 0, 0, 0) };
            genTraits.Click += (_, _) => traitsSeed.Text = "0x" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
            Grid.SetColumn(genTraits, 2); traitsRow.Children.Add(genTraits); root.Children.Add(traitsRow);
            void ResourceRow(string label, string key)
            {
                var res = pilot.GetObject(key); var box = new TextBox { Text = res?.GetString("Filename") ?? "" };
                box.LostFocus += (_, _) => { res?.Set("Filename", box.Text); Changed(); };
                Row(label, box);
            }
            ResourceRow("NPC Resource", "NPCResource");
            ResourceRow("Ship Resource", "ShipResource");
            bool isUnlocked = unlocked is not null && index < unlocked.Length && unlocked.GetBool(index);
            var unlockedBox = new CheckBox { Content = "Slot Unlocked", IsChecked = isUnlocked, Margin = new Thickness(3, 7, 3, 7) };
            unlockedBox.Click += (_, _) => { if (unlocked is not null && index < unlocked.Length) { unlocked.Set(index, unlockedBox.IsChecked == true); _session.MarkDirty(); } };
            root.Children.Add(unlockedBox);
            details.Content = new Border { Style = PageStyle("Card"), Padding = new Thickness(10), Child = root };
        }
        list.SelectionChanged += (_, _) => ShowSelected();
        Button Action(string label, Action click, bool primary = false)
        {
            var b = new Button { Content = label, Margin = new Thickness(2) };
            if (primary) b.Style = PageStyle("Primary");
            b.Click += (_, _) => click(); actions.Children.Add(b); return b;
        }
        Action("Delete", () =>
        {
            int index = SelectedChoice()?.Index ?? -1; if (index < 0 || index >= pilots.Length) return;
            if (MessageBox.Show(_owner, UiStrings.LocalizeText("Clear the selected squadron pilot slot?"), UiStrings.LocalizeText("Delete Pilot"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            SquadronLogic.DeletePilot(pilots.GetObject(index)); _session.MarkDirty(); UpdateList(index); ShowSelected();
        });
        Action("Export", () => ExportFleetRecord(pilots, SelectedChoice()?.Index ?? -1, "Squadron Pilot JSON", "squadron-pilot.json"));
        Action("Import", () =>
        {
            var dialog = new OpenFileDialog { Filter = "Squadron files|*.sqd|JSON files|*.json|All files|*.*" };
            if (dialog.ShowDialog(_owner) != true) return;
            try
            {
                var imported = JsonObject.ImportFromFile(dialog.FileName);
                if (imported.GetObject("Data") is JsonObject data && data.GetObject("Pilot") is JsonObject wrapped) imported = wrapped;
                int target = Enumerable.Range(0, pilots.Length).FirstOrDefault(i =>
                {
                    var current = pilots.GetObject(i);
                    string filename = current?.GetObject("NPCResource")?.GetString("Filename") ?? "";
                    return string.IsNullOrWhiteSpace(filename) || filename == "^";
                }, -1);
                if (target < 0) { _owner.PageError(new InvalidOperationException("No empty squadron slot is available.")); return; }
                foreach (string name in imported.Names()) pilots.GetObject(target)?.Set(name, imported.Get(name));
                _session.MarkDirty(); UpdateList(target); ShowSelected();
            }
            catch (Exception ex) { _owner.PageError(ex); }
        });
        var right = new DockPanel();
        DockPanel.SetDock(actions, Dock.Bottom); right.Children.Add(details); right.Children.Add(actions);
        UpdateList(0); ShowSelected();
        Grid.SetColumn(right, 1); layout.Children.Add(left); layout.Children.Add(right);
        return layout;
    }

    void ExportFleetRecord(JsonArray records, int index, string title, string defaultName)
    {
        if (index < 0 || index >= records.Length || records.GetObject(index) is not { } record) return;
        var dialog = new SaveFileDialog { Title = UiStrings.LocalizeText(title), FileName = defaultName, Filter = UiStrings.LocalizeText("JSON files|*.json|All files|*.*"), DefaultExt = ".json", AddExtension = true };
        if (dialog.ShowDialog(_owner) != true) return;
        try { File.WriteAllText(dialog.FileName, record.ToFormattedString()); _owner.PageStatus($"{title} exported."); }
        catch (Exception ex) { _owner.PageError(ex); }
    }

    void FinishFleetExpedition(JsonObject expedition, JsonArray frigates)
    {
        foreach (string key in new[] { "DamagedFrigateIndices", "DestroyedFrigateIndices", "ActiveFrigateIndices" })
        {
            var array = expedition.GetArray(key);
            if (array is null) continue;
            while (array.Length > 0) array.RemoveAt(array.Length - 1);
        }
        var all = expedition.GetArray("AllFrigateIndices");
        var active = expedition.GetArray("ActiveFrigateIndices");
        if (all is not null && active is not null) for (int i = 0; i < all.Length; i++) active.Add(all.GetInt(i));
        if (expedition.GetArray("Events") is { } events)
        {
            expedition.Set("NextEventToTrigger", events.Length);
            expedition.Set("NumberOfSuccessfulEventsThisExpedition", events.Length);
            expedition.Set("NumberOfFailedEventsThisExpedition", 0);
            for (int i = 0; i < events.Length; i++) events.GetObject(i)?.Set("Success", true);
        }
        expedition.Set("PauseTime", 0);
        if (all is not null)
            for (int i = 0; i < all.Length; i++)
            {
                int index = all.GetInt(i);
                if (index >= 0 && index < frigates.Length && frigates.GetObject(index) is { } f)
                {
                    f.Set("DamageTaken", 0);
                    f.Set("RepairsMade", 0);
                }
            }
    }
}
