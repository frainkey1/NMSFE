using System.Globalization;
using System.IO;
using System.Windows.Controls.Primitives;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NMSE.Core;
using NMSE.Data;
using NMSE.Models;
using NMSE.Next.Engine;

namespace NMSE.Next.Desktop;

internal sealed class SettlementsPageView : Grid
{
    sealed record PageTab(string Name);
    sealed record PerkChoice(string Id, string Name, string Effect, bool Procedural)
    {
        public override string ToString() => string.IsNullOrEmpty(Id) ? UiStrings.LocalizeText("(None)") :
            string.IsNullOrEmpty(Effect) ? UiStrings.LocalizeText(Name) : $"{UiStrings.LocalizeText(Name)} ({UiStrings.LocalizeText(Effect)})";
    }
    sealed record ItemChoice(string Id, string Name)
    {
        public override string ToString() => UiStrings.LocalizeText(Name);
    }
    sealed record MilestoneChoice(int Value, string Label)
    {
        public override string ToString() => $"{Value} · {Label}";
    }
    sealed record DateEntry(string Key, DatePicker Picker);

    static readonly string[] Tabs = ["Stats & Perks", "Production", "Building States", "Building Editor"];
    static readonly string[] StatLocKeys = ["settlement.max_population", "settlement.happiness", "settlement.productivity", "settlement.upkeep", "settlement.sentinels", "settlement.debt", "settlement.alert", "settlement.bug_attack"];
    static readonly string[] DecisionKeys = ["None", "StrangerVisit", "Policy", "NewBuilding", "BuildingChoice", "Conflict", "Request", "BlessingPerkRelated", "JobPerkRelated", "ProcPerkRelated", "UpgradeBuilding", "UpgradeBuildingChoice"];
    static readonly string[] RaceKeys = ["Traders", "Warriors", "Explorers", "Robots", "Atlas", "Diplomats", "Exotics", "None", "Builders"];
    static readonly string[] RaceNames = ["Gek", "Vy'keen", "Korvax", "Robots", "Atlas", "Diplomats", "Exotics", "None", "Autophage"];

    readonly SaveSession _session;
    readonly Action<string> _status;
    readonly Action<Exception> _error;
    readonly Action<string[]> _navigateJson;
    readonly ListBox _records = new();
    readonly TextBox _search = new();
    readonly TextBlock _count = new();
    readonly TextBlock _heading = new();
    readonly TextBlock _empty = new();
    readonly StackPanel _body = new();
    readonly Button _apply = new() { Content = "✓  Apply changes", MinWidth = 150 };
    Button? _selectedJson;
    readonly List<Button> _tabButtons = [];
    readonly Dictionary<string, TextBox> _text = new(StringComparer.Ordinal);
    readonly Dictionary<string, ComboBox> _combo = new(StringComparer.Ordinal);
    readonly List<DateEntry> _dates = [];
    readonly List<(TextBox Value, TextBlock Description, ComboBox? Preset)> _buildingRows = [];
    readonly List<(ComboBox Item, TextBox Amount)> _productionRows = [];
    readonly List<(ComboBox Choice, TextBox Seed)> _perkRows = [];
    readonly List<string> _perkRaw = [];
    readonly List<CheckBox> _editorBits = [];
    readonly List<int> _editorBitOffsets = [];
    readonly List<SettlementEntry> _entries = [];
    JsonObject? _draft;
    int _selectedIndex = -1;
    string _activeTab = Tabs[0];
    int _editorSlot;
    bool _binding;
    bool _pending;

    public bool HasPending => _pending;

    public SettlementsPageView(SaveSession session, Action<string> status, Action<Exception> error, Action<string[]> navigateJson)
    {
        _session = session;
        _status = status;
        _error = error;
        _navigateJson = navigateJson;

        Margin = new Thickness(8);
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });

        var title = new StackPanel { Margin = new Thickness(4, 2, 4, 8) };
        title.Children.Add(new TextBlock { Text = "Settlements", Style = StyleOf("Heading") });
        title.Children.Add(new TextBlock { Text = "Edit settlement statistics, perks, production, and building states.", Margin = new Thickness(0, 2, 0, 0) });
        var jsonLinks = new WrapPanel { Margin = new Thickness(0, 5, 0, 0) };
        jsonLinks.Children.Add(new Button { Content = UiStrings.Get("settlement.json_all"), ToolTip = UiStrings.Get("settlement.json_all_tip"), Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(2) });
        ((Button)jsonLinks.Children[0]).Click += (_, _) => _navigateJson(["PlayerStateData", "SettlementStatesV2"]);
        _selectedJson = new Button { Content = UiStrings.Get("settlement.json_selected"), ToolTip = UiStrings.Get("settlement.json_selected_tip"), Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(2), IsEnabled = false };
        _selectedJson.Click += (_, _) => { if (_selectedIndex >= 0) _navigateJson(["PlayerStateData", "SettlementStatesV2", $"[{_selectedIndex}]"]); };
        jsonLinks.Children.Add(_selectedJson);
        title.Children.Add(jsonLinks);
        Children.Add(title);

        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 4, 9) };
        foreach (string tab in Tabs)
        {
            var button = new Button { Content = UiStrings.LocalizeText(tab), Tag = tab, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 7, 0) };
            button.Click += (_, _) => ChangeTab(tab);
            _tabButtons.Add(button);
            tabs.Children.Add(button);
        }
        Grid.SetRow(tabs, 1);
        Children.Add(tabs);

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new() { Width = new GridLength(275) });
        layout.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(layout, 2);
        Children.Add(layout);

        var left = new Grid { Margin = new Thickness(0, 0, 10, 0) };
        left.RowDefinitions.Add(new() { Height = GridLength.Auto });
        left.RowDefinitions.Add(new() { Height = GridLength.Auto });
        left.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        left.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _search.Margin = new Thickness(3, 3, 3, 8);
        _search.Padding = new Thickness(7, 5, 7, 5);
        _search.SetResourceReference(Control.BackgroundProperty, "Input");
        _search.SetResourceReference(Control.ForegroundProperty, "Text");
        _search.SetResourceReference(Control.BorderBrushProperty, "Edge");
        _search.TextChanged += (_, _) => RefreshRecordList(_selectedIndex);
        left.Children.Add(_search);
        _count.Margin = new Thickness(6, 0, 4, 8);
        Grid.SetRow(_count, 1);
        left.Children.Add(_count);
        _records.SelectionChanged += RecordSelected;
        _records.DisplayMemberPath = nameof(SettlementEntry.Name);
        _records.SetResourceReference(Control.BackgroundProperty, "Input");
        _records.SetResourceReference(Control.ForegroundProperty, "Text");
        _records.SetResourceReference(Control.BorderBrushProperty, "Edge");
        Grid.SetRow(_records, 2);
        left.Children.Add(_records);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        AddAction(actions, "Delete", DeleteSettlement);
        AddAction(actions, "Export…", ExportSettlement);
        AddAction(actions, "Import…", ImportSettlement);
        Grid.SetRow(actions, 3);
        left.Children.Add(actions);
        var leftCard = new Border { Style = StyleOf("Card"), Padding = new Thickness(8), Child = left };
        layout.Children.Add(leftCard);

        var right = new Grid();
        right.RowDefinitions.Add(new() { Height = GridLength.Auto });
        right.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        right.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _heading.Text = UiStrings.Get("settlement.select");
        _heading.Style = StyleOf("Heading");
        _heading.Margin = new Thickness(4, 1, 4, 10);
        right.Children.Add(_heading);
        var bodyScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        bodyScroll.Content = _body;
        Grid.SetRow(bodyScroll, 1);
        right.Children.Add(bodyScroll);
        var footer = new WrapPanel { Margin = new Thickness(2, 9, 2, 1), VerticalAlignment = VerticalAlignment.Center };
        _apply.Style = StyleOf("Primary");
        _apply.Click += (_, _) => ApplyChanges();
        footer.Children.Add(_apply);
        var hint = new TextBlock { Text = UiStrings.Get("settlement.footer_hint"), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Secondary");
        footer.Children.Add(hint);
        Grid.SetRow(footer, 2);
        right.Children.Add(footer);
        var rightCard = new Border { Style = StyleOf("Card"), Padding = new Thickness(14), Child = right };
        Grid.SetColumn(rightCard, 1);
        layout.Children.Add(rightCard);

        RefreshRecordList(-1);
        UpdateTabStyles();
        ShowCurrent();
    }

    void AddAction(Panel panel, string label, RoutedEventHandler handler)
    {
        var button = new Button { Content = UiStrings.LocalizeText(label), Margin = new Thickness(2), Padding = new Thickness(5, 6, 5, 6) };
        button.Click += handler;
        panel.Children.Add(button);
    }

    void RefreshRecordList(int selectIndex)
    {
        _entries.Clear();
        _entries.AddRange(_session.GetSettlements());
        string query = _search.Text.Trim();
        var shown = _entries.Where(x => query.Length == 0 || x.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || x.Seed.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        _binding = true;
        _records.ItemsSource = shown;
        _count.Text = UiStrings.Format("settlement.records_count",shown.Length,_entries.Count);
        _records.SelectedItem = shown.FirstOrDefault(x => x.Index == selectIndex);
        _binding = false;
        if (_records.SelectedItem is SettlementEntry entry && _draft == null)
            LoadSettlement(entry.Index);
    }

    void RecordSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_binding || _records.SelectedItem is not SettlementEntry entry || entry.Index == _selectedIndex) return;
        if (!Commit())
        {
            _binding = true;
            _records.SelectedItem = _entries.FirstOrDefault(x => x.Index == _selectedIndex);
            _binding = false;
            return;
        }
        LoadSettlement(entry.Index);
    }

    void LoadSettlement(int index)
    {
        try
        {
            _draft = _session.ReadSettlement(index);
            _selectedIndex = index;
            if (_selectedJson != null) _selectedJson.IsEnabled = true;
            _pending = false;
            _heading.Text = UiStrings.LocalizeText(ReadString(_draft, "Name", UiStrings.Format("settlement.empty_name",index + 1)));
            ShowCurrent();
        }
        catch (Exception ex) { _error(ex); }
    }

    void ChangeTab(string tab)
    {
        if (tab == _activeTab) return;
        if (!CaptureCurrent()) return;
        _activeTab = tab;
        UpdateTabStyles();
        ShowCurrent();
    }

    void UpdateTabStyles()
    {
        foreach (var button in _tabButtons)
        {
            bool selected = (string)button.Tag == _activeTab;
            button.SetResourceReference(Control.BackgroundProperty, selected ? "AccentSurface" : "Surface");
            button.SetResourceReference(Control.ForegroundProperty, selected ? "OnAccent" : "Text");
            button.SetResourceReference(Control.BorderBrushProperty, selected ? "Accent" : "Edge");
            button.BorderThickness = new Thickness(selected ? 1 : 0.5);
        }
    }

    void ShowCurrent()
    {
        _body.Children.Clear();
        _text.Clear();
        _combo.Clear();
        _dates.Clear();
        _buildingRows.Clear();
        _productionRows.Clear();
        _perkRows.Clear();
        _perkRaw.Clear();
        _editorBits.Clear();
        _editorBitOffsets.Clear();

        bool available = _draft != null;
        _apply.IsEnabled = available;
        _empty.Visibility = available ? Visibility.Collapsed : Visibility.Visible;
        if (!available)
        {
            _body.Children.Add(EmptyState(UiStrings.Get("settlement.empty")));
            _heading.Text = UiStrings.Get("settlement.title");
            return;
        }
        _heading.Text = ReadString(_draft!, "Name", $"Settlement {_selectedIndex + 1}");
        switch (_activeTab)
        {
            case "Stats & Perks": RenderStatsAndPerks(); break;
            case "Production": RenderProduction(); break;
            case "Building States": RenderBuildingStates(); break;
            case "Building Editor": RenderBuildingEditor(); break;
        }
    }

    void RenderStatsAndPerks()
    {
        var top = NewWrap();
        var identity = NewCard("Settlement");
        AddTextField(Inner(identity), "Name", ReadString(_draft!, "Name", ""), "Name");
        AddTextField(Inner(identity), "Seed", ReadString(_draft!, "SeedValue", ""), "SeedValue");
        AddComboField(Inner(identity), "NPC race", RaceOptions(), ReadString(_draft!.GetObject("Race"), "AlienRace", "None"), "Race");
        var population = ReadInt(_draft!, "Population", 0);
        if (_draft!.Contains("Population"))
            AddTextField(Inner(identity), "Population", population.ToString(CultureInfo.InvariantCulture), "Population");
        top.Children.Add(identity);

        var stats = NewCard("Settlement stats");
        var statArray = _draft!.GetArray("Stats");
        for (int i = 0; i < 8; i++)
        {
            string label = UiStrings.Get(StatLocKeys[i]);
            AddTextField(Inner(stats), label, ReadArrayInt(statArray, i).ToString(CultureInfo.InvariantCulture), $"Stats:{i}");
        }
        top.Children.Add(stats);

        var decisions = NewCard("Judgement & activity");
        var decisionObj = _draft!.GetObject("PendingJudgementType");
        AddComboField(Inner(decisions), "Decision type", DecisionOptions(), ReadString(decisionObj, "SettlementJudgementType", "None"), "Decision");
        AddDateField(Inner(decisions), "Last judgement", "LastJudgementTime");
        AddDateField(Inner(decisions), "Last population change", "LastPopulationChangeTime");
        AddDateField(Inner(decisions), "Last bug attack", "LastBugAttackChangeTime");
        AddDateField(Inner(decisions), "Last alert", "LastAlertChangeTime");
        AddDateField(Inner(decisions), "Last debt", "LastDebtChangeTime");
        AddDateField(Inner(decisions), "Last upkeep check", "LastUpkeepDebtCheckTime");
        AddTextField(Inner(decisions), "Mission seed", ReadInt(_draft!, "MiniMissionSeed", 0).ToString(CultureInfo.InvariantCulture), "MiniMissionSeed");
        AddDateField(Inner(decisions), "Mission start time", "MiniMissionStartTime");
        top.Children.Add(decisions);

        _body.Children.Add(top);

        var perksCard = NewCard("Settlement perks · 18 slots");
        var perks = _draft!.GetArray("Perks");
        var choices = PerkOptions();
        for (int i = 0; i < 18; i++)
        {
            string raw = i < (perks?.Length ?? 0) ? ReadArrayString(perks, i, "^") : "^";
            string id = raw;
            string seed = "";
            _perkRaw.Add(raw);
            int hash = raw.IndexOf('#');
            if (hash >= 0) { id = raw[..hash]; seed = raw[(hash + 1)..]; }
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(54) });
            row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new() { Width = new GridLength(115) });
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = UiStrings.Format("settlement.perk_index", i + 1), VerticalAlignment = VerticalAlignment.Center });
            var combo = MakeCombo(choices, "Perk:" + i);
            combo.SelectedItem = choices.OfType<PerkChoice>().FirstOrDefault(x => x.Id == id) ?? choices.OfType<PerkChoice>().First();
            Grid.SetColumn(combo, 1); row.Children.Add(combo);
            var seedBox = MakeText("PerkSeed:" + i, seed);
            seedBox.Margin = new Thickness(5, 0, 4, 0);
            seedBox.Visibility = (combo.SelectedItem as PerkChoice)?.Procedural == true ? Visibility.Visible : Visibility.Collapsed;
            combo.SelectionChanged += (_, _) => { seedBox.Visibility = (combo.SelectedItem as PerkChoice)?.Procedural == true ? Visibility.Visible : Visibility.Collapsed; MarkPending(); };
            Grid.SetColumn(seedBox, 2); row.Children.Add(seedBox);
            var clear = new Button { Content = "×", ToolTip = "Clear this perk to (None)", MinWidth = 30, Padding = new Thickness(4, 1, 4, 1), Margin = new Thickness(2, 0, 0, 0) };
            clear.Click += (_, _) => { combo.SelectedIndex = 0; seedBox.Clear(); MarkPending(); };
            Grid.SetColumn(clear, 3); row.Children.Add(clear);
            Inner(perksCard).Children.Add(row);
            _perkRows.Add((combo, seedBox));
        }
        _body.Children.Add(perksCard);
    }

    void RenderProduction()
    {
        var card = NewCard("Production outputs");
        var intro = new TextBlock { Text = UiStrings.Get("settlement.production_hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        intro.SetResourceReference(TextBlock.ForegroundProperty, "Secondary");
        Inner(card).Children.Add(intro);
        var array = _draft!.GetArray("ProductionState");
        var allChoices = ProductionOptions();
        for (int i = 0; i < (array?.Length ?? 0); i++)
        {
            var obj = array!.GetObject(i);
            string id = ReadString(obj, "ElementId", "");
            string lookup = id.StartsWith('^') ? id[1..] : id;
            string name = _session.Items.GetItem(lookup)?.Name ?? lookup;
            var choices = allChoices.ToList();
            if (!string.IsNullOrEmpty(lookup) && choices.OfType<ItemChoice>().All(x => x.Id != lookup)) choices.Insert(0, new ItemChoice(lookup, UiStrings.Format("settlement.unknown_item",lookup)));
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new() { Width = new GridLength(105) });
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var combo = MakeCombo(choices, "ProdId:" + i);
            combo.SelectedItem = choices.OfType<ItemChoice>().FirstOrDefault(x => x.Id == lookup) ?? choices.OfType<ItemChoice>().FirstOrDefault();
            row.Children.Add(combo);
            var amount = MakeText("ProdAmount:" + i, ReadInt(obj, "Amount", 0).ToString(CultureInfo.InvariantCulture));
            amount.Margin = new Thickness(5, 0, 5, 0);
            Grid.SetColumn(amount, 1); row.Children.Add(amount);
            var remove = new Button { Content = "×", ToolTip = UiStrings.Get("settlement.remove_output"), MinWidth = 30 };
            int index = i;
            remove.Click += (_, _) => { if (!CaptureCurrent()) return; var arr = _draft!.GetArray("ProductionState"); arr?.RemoveAt(index); MarkPending(); ShowCurrent(); };
            Grid.SetColumn(remove, 2); row.Children.Add(remove);
            Inner(card).Children.Add(row);
            _productionRows.Add((combo, amount));
            _ = name;
        }
        var add = new Button { Content = UiStrings.Get("settlement.add_output"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(1, 9, 0, 0) };
        add.Click += (_, _) =>
        {
            if (!CaptureCurrent()) return;
            var arr = _draft!.GetArray("ProductionState");
            if (arr == null) { arr = new JsonArray(); _draft.Set("ProductionState", arr); }
            var item = new JsonObject(); item.Set("ElementId", "^"); item.Set("Amount", 0); arr.Add(item);
            MarkPending(); ShowCurrent();
        };
        Inner(card).Children.Add(add);
        _body.Children.Add(card);
    }

    void RenderBuildingStates()
    {
        var card = NewCard("Building states · 48 slots");
        var warning = new TextBlock { Text = UiStrings.Get("settlement.building_states_hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        warning.SetResourceReference(TextBlock.ForegroundProperty, "Secondary");
        Inner(card).Children.Add(warning);
        var arr = _draft!.GetArray("BuildingStates");
        var milestones = SettlementOperations.BuildingMilestones.Select(x => new MilestoneChoice(x.Value, x.Label)).ToArray();
        for (int i = 0; i < 48; i++)
        {
            int value = ReadArrayInt(arr, i);
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(63) });
            row.ColumnDefinitions.Add(new() { Width = new GridLength(145) });
            row.ColumnDefinitions.Add(new() { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock { Text = UiStrings.Format("settlement.slot_label",i + 1), VerticalAlignment = VerticalAlignment.Center });
            var combo = MakeCombo(milestones, "BuildPreset:" + i);
            combo.SelectedItem = milestones.FirstOrDefault(x => x.Value == value);
            Grid.SetColumn(combo, 1); row.Children.Add(combo);
            var number = MakeText("Building:" + i, value.ToString(CultureInfo.InvariantCulture));
            number.Margin = new Thickness(5, 0, 5, 0);
            Grid.SetColumn(number, 2); row.Children.Add(number);
            var desc = new TextBlock { Text = UiStrings.LocalizeText(SettlementOperations.DescribeBuildingState(value)), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, FontSize = 11 };
            desc.SetResourceReference(TextBlock.ForegroundProperty, "Secondary");
            Grid.SetColumn(desc, 3); row.Children.Add(desc);
            int slot = i;
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is MilestoneChoice preset) number.Text = preset.Value.ToString(CultureInfo.InvariantCulture);
                MarkPending();
            };
            number.TextChanged += (_, _) =>
            {
                if (int.TryParse(number.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                    desc.Text = UiStrings.LocalizeText(SettlementOperations.DescribeBuildingState(v));
                MarkPending();
            };
            Inner(card).Children.Add(row);
            _buildingRows.Add((number, desc, combo));
        }
        _body.Children.Add(card);
    }

    void RenderBuildingEditor()
    {
        var card = NewCard("Building state bit editor");
        var note = new TextBlock { Text = UiStrings.Get("settlement.building_editor_hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 9) };
        note.SetResourceReference(TextBlock.ForegroundProperty, "Secondary"); Inner(card).Children.Add(note);

        var slots = Enumerable.Range(1, 48).Select(x => UiStrings.Format("settlement.slot_label",x)).ToArray();
        var selector = MakeCombo(slots, "EditorSlot");
        selector.SelectedIndex = Math.Clamp(_editorSlot, 0, 47);
        selector.SelectionChanged += (_, _) =>
        {
            if (!CaptureEditorRaw()) return;
            _editorSlot = selector.SelectedIndex;
            LoadEditorBits();
        };
        Inner(card).Children.Add(Labeled("Building slot", selector));
        var raw = MakeText("EditorRaw", ReadArrayInt(_draft!.GetArray("BuildingStates"), _editorSlot).ToString(CultureInfo.InvariantCulture));
        raw.TextChanged += (_, _) => { if (int.TryParse(raw.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)) SyncBits(value); MarkPending(); };
        Inner(card).Children.Add(Labeled("Raw state value", raw));

        var decoded = new WrapPanel { Margin = new Thickness(0, 4, 0, 7) };
        var (className, stateName) = SettlementOperations.DecodeBuildingState(ReadArrayInt(_draft!.GetArray("BuildingStates"), _editorSlot));
        var classText = new TextBlock { Text = UiStrings.Format("settlement.class_value", className), Margin = new Thickness(3, 2, 16, 2), FontWeight = FontWeights.SemiBold };
        var stateText = new TextBlock { Text = UiStrings.Format("settlement.state_value", stateName), Margin = new Thickness(3, 2, 3, 2), FontWeight = FontWeights.SemiBold };
        decoded.Children.Add(classText); decoded.Children.Add(stateText); Inner(card).Children.Add(decoded);

        AddBitGroup(Inner(card), "Initial construction phases", Enumerable.Range(0, 7).Select(i => UiStrings.Format("settlement.phase_label",i + 1)).ToArray(), Enumerable.Range(0, 7).Select(i => i).ToArray());
        AddBitGroup(Inner(card), "Upgrade progress", Enumerable.Range(0, 10).Select(i => UiStrings.Format("settlement.phase_label",i + 1)).ToArray(), Enumerable.Range(0, 10).Select(i => 10 + i).ToArray());
        AddBitGroup(Inner(card), "Tier progression", ["B started", "B confirmed", "A started", "A confirmed", "S started", "S confirmed"], Enumerable.Range(20, 6).ToArray());
        AddBitGroup(Inner(card), "Class and arrival flags", ["Class system active", "B arrived", "A arrived", "S arrived"], Enumerable.Range(26, 4).ToArray());

        foreach (var bit in _editorBits)
        {
            bit.Checked += (_, _) => UpdateBitPreview(raw, classText, stateText);
            bit.Unchecked += (_, _) => UpdateBitPreview(raw, classText, stateText);
        }
        var applySlot = new Button { Content = UiStrings.Get("settlement.apply_to_slot"), Style = StyleOf("Primary"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(1, 10, 0, 0) };
        applySlot.Click += (_, _) => { if (!CaptureEditorRaw()) return; var arr = EnsureArray("BuildingStates", 48); arr.Set(_editorSlot, ComposeBits()); MarkPending(); _status(UiStrings.Get("settlement.staged_status")); };
        Inner(card).Children.Add(applySlot);
        _body.Children.Add(card);
        LoadEditorBits();
    }

    void AddBitGroup(Panel target, string title, string[] labels, int[] offsets)
    {
        var group = new StackPanel { Margin = new Thickness(1, 5, 4, 4) };
        group.Children.Add(new TextBlock { Text = UiStrings.LocalizeText(title), FontWeight = FontWeights.SemiBold, Margin = new Thickness(1, 2, 1, 4) });
        var wrap = new WrapPanel();
        for (int i = 0; i < labels.Length; i++)
        {
            var check = new CheckBox { Content = UiStrings.LocalizeText(labels[i]), Tag = offsets[i], Margin = new Thickness(4, 3, 14, 3) };
            wrap.Children.Add(check); _editorBits.Add(check); _editorBitOffsets.Add(offsets[i]);
        }
        group.Children.Add(wrap); target.Children.Add(group);
    }

    void LoadEditorBits()
    {
        bool prior = _binding; _binding = true;
        try
        {
            int value = ReadArrayInt(_draft!.GetArray("BuildingStates"), _editorSlot);
            for (int i = 0; i < _editorBits.Count; i++)
                _editorBits[i].IsChecked = (value & (1 << _editorBitOffsets[i])) != 0;
            if (_text.TryGetValue("EditorRaw", out var raw)) raw.Text = value.ToString(CultureInfo.InvariantCulture);
        }
        finally { _binding = prior; }
    }

    bool CaptureEditorRaw()
    {
        if (_text.TryGetValue("EditorRaw", out var raw))
        {
            if (!int.TryParse(raw.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            { _status(UiStrings.Get("settlement.invalid_state")); raw.Focus(); return false; }
            EnsureArray("BuildingStates", 48).Set(_editorSlot, value);
            SyncBits(value);
        }
        return true;
    }

    int ComposeBits()
    {
        int value = 0;
        for (int i = 0; i < _editorBits.Count; i++)
            if (_editorBits[i].IsChecked == true) value |= 1 << _editorBitOffsets[i];
        return value;
    }

    void SyncBits(int value)
    {
        for (int i = 0; i < _editorBits.Count; i++)
            _editorBits[i].IsChecked = (value & (1 << _editorBitOffsets[i])) != 0;
    }

    Border NewCard(string title)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = UiStrings.LocalizeText(title), FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        return new Border { Style = StyleOf("Card"), Padding = new Thickness(11), Margin = new Thickness(1, 0, 10, 10), MinWidth = 300, MaxWidth = 720, Child = panel };
    }

    WrapPanel NewWrap() => new() { Orientation = Orientation.Horizontal };

    void AddTextField(Panel panel, string label, string value, string key)
    {
        panel.Children.Add(Labeled(label, MakeText(key, value)));
    }

    TextBox MakeText(string key, string value)
    {
        var box = new TextBox { Text = value, MinWidth = 92, Padding = new Thickness(6, 4, 6, 4), VerticalContentAlignment = VerticalAlignment.Center, Tag = key, Margin = new Thickness(1, 2, 1, 2) };
        box.SetResourceReference(Control.BackgroundProperty, "Input");
        box.SetResourceReference(Control.ForegroundProperty, "Text");
        box.SetResourceReference(Control.BorderBrushProperty, "Edge");
        box.TextChanged += (_, _) => MarkPending();
        _text[key] = box;
        return box;
    }

    void AddComboField(Panel panel, string label, IEnumerable<object> choices, string value, string key)
    {
        var combo = MakeCombo(choices, key);
        combo.SelectedItem = combo.Items.Cast<KeyValuePair<string, string>>().FirstOrDefault(x => x.Key == value);
        if (combo.SelectedIndex < 0) combo.SelectedIndex = 0;
        panel.Children.Add(Labeled(label, combo));
    }

    ComboBox MakeCombo(IEnumerable<object> choices, string key)
    {
        var combo = new ComboBox { ItemsSource = choices.ToArray(), MinWidth = 150, Padding = new Thickness(5, 4, 5, 4), Tag = key, Margin = new Thickness(1, 2, 1, 2) };
        if (choices.FirstOrDefault() is KeyValuePair<string, string>)
        {
            combo.DisplayMemberPath = "Value";
            combo.SelectedValuePath = "Key";
        }
        combo.SetResourceReference(Control.BackgroundProperty, "Input");
        combo.SetResourceReference(Control.ForegroundProperty, "Text");
        combo.SetResourceReference(Control.BorderBrushProperty, "Edge");
        _combo[key] = combo;
        combo.SelectionChanged += (_, _) => MarkPending();
        return combo;
    }

    void AddDateField(Panel panel, string label, string key)
    {
        long seconds = ReadLong(_draft!, key, 0);
        DateTime? selected = null;
        if (seconds > 0)
        {
            try { selected = DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime; } catch (ArgumentOutOfRangeException) { }
        }
        var picker = new DatePicker { SelectedDate = selected, MinWidth = 155, Margin = new Thickness(1, 2, 1, 2), Tag = key };
        picker.SelectedDateChanged += (_, _) => MarkPending();
        _dates.Add(new DateEntry(key, picker));
        panel.Children.Add(Labeled(label, picker));
    }

    static Border Labeled(string label, Control control)
    {
        var grid = new Grid { Margin = new Thickness(0, 1, 2, 1) };
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(145) });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var text = new TextBlock { Text = UiStrings.LocalizeText(label), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(1, 4, 7, 4), TextWrapping = TextWrapping.Wrap };
        grid.Children.Add(text);
        Grid.SetColumn(control, 1); grid.Children.Add(control);
        return new Border { Child = grid };
    }

    IEnumerable<object> RaceOptions()
    {
        for (int i = 0; i < RaceKeys.Length; i++)
            yield return new KeyValuePair<string, string>(RaceKeys[i], $"{RaceNames[i]} ({RaceKeys[i]})");
    }

    IEnumerable<object> DecisionOptions()
    {
        foreach (string key in DecisionKeys)
        {
            string loc = "settlement.decision_" + key.ToLowerInvariant();
            string label = key == "None" ? "None" : UiStrings.Get(loc);
            if (string.IsNullOrWhiteSpace(label) || label == loc) label = key;
            yield return new KeyValuePair<string, string>(key, label);
        }
    }

    IEnumerable<object> PerkOptions()
    {
        yield return new PerkChoice("", "(None)", "", false);
        foreach (var p in SettlementOperations.PerkChoices)
            yield return new PerkChoice(p.Id, p.Name, p.Effect, p.Procedural);
    }

    IEnumerable<object> ProductionOptions()
    {
        var products = _session.Items.Items.Values
            .Where(i => i.ItemType.Equals("Products", StringComparison.OrdinalIgnoreCase) || i.ItemType.Equals("Raw Materials", StringComparison.OrdinalIgnoreCase))
            .OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(i => new ItemChoice(i.Id, i.Name));
        yield return new ItemChoice("", "(None)");
        foreach (var item in products) yield return item;
    }

    void MarkPending()
    {
        if (_binding) return;
        _pending = true;
        _apply.IsEnabled = _draft != null;
    }

    bool CaptureCurrent()
    {
        if (_draft == null) return true;
        foreach (var (key, box) in _text.ToArray())
        {
            string value = box.Text;
            try
            {
                if (key == "Name") _draft.Set("Name", value);
                else if (key == "SeedValue") _draft.Set("SeedValue", value);
                else if (key == "Population") _draft.Set("Population", ParseInt(value, "Population"));
                else if (key.StartsWith("Stats:", StringComparison.Ordinal))
                    EnsureArray("Stats", 8).Set(int.Parse(key.AsSpan(6), CultureInfo.InvariantCulture), ParseInt(value, "settlement stat"));
                else if (key == "MiniMissionSeed") _draft.Set("MiniMissionSeed", ParseInt(value, "Mission seed"));
                else if (key.StartsWith("ProdAmount:", StringComparison.Ordinal) || key.StartsWith("Building:", StringComparison.Ordinal))
                    _ = ParseInt(value, key.StartsWith("ProdAmount:", StringComparison.Ordinal) ? "production amount" : "building state");
                else if (key.StartsWith("PerkSeed:", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(value))
                    _ = ParseInt(value, "perk seed");
                else if (key == "EditorRaw") _ = ParseInt(value, "building state");
            }
            catch (Exception ex) { _status(ex.Message); box.Focus(); box.SelectAll(); return false; }
        }

        if (_combo.TryGetValue("Race", out var race) && race.SelectedItem is KeyValuePair<string, string> raceChoice)
        {
            var obj = EnsureObject("Race"); obj.Set("AlienRace", raceChoice.Key);
        }
        if (_combo.TryGetValue("Decision", out var decision) && decision.SelectedItem is KeyValuePair<string, string> decisionChoice)
            EnsureObject("PendingJudgementType").Set("SettlementJudgementType", decisionChoice.Key);

        var perks = _draft.GetArray("Perks");
        if (_perkRows.Count > 0)
        {
            for (int i = 0; i < _perkRows.Count; i++)
            {
                var (choice, seedBox) = _perkRows[i];
                var selected = choice.SelectedItem as PerkChoice;
                string raw = SettlementOperations.FormatPerk(selected?.Id ?? "", seedBox.Text);
                if (raw == _perkRaw[i]) continue;
                if (perks == null) { perks = new JsonArray(); _draft.Set("Perks", perks); }
                if (i < perks.Length) perks.Set(i, raw); else perks.Add(raw);
            }
        }

        if (_productionRows.Count > 0 || _activeTab == "Production")
        {
            var old = _draft.GetArray("ProductionState");
            var updated = new JsonArray();
            for (int i = 0; i < _productionRows.Count; i++)
            {
                var (combo, amountBox) = _productionRows[i];
                var selected = combo.SelectedItem as ItemChoice;
                if (selected == null || string.IsNullOrEmpty(selected.Id)) continue;
                int amount = ParseInt(amountBox.Text, "production amount");
                var original = i < (old?.Length ?? 0) ? old!.GetObject(i) : null;
                string originalId = ReadString(original, "ElementId", "");
                if (original != null && originalId.TrimStart('^') == selected.Id && ReadInt(original, "Amount", 0) == amount)
                {
                    updated.Add(original.DeepClone());
                    continue;
                }
                var item = original?.DeepClone() ?? new JsonObject();
                item.Set("ElementId", selected.Id.StartsWith('^') ? selected.Id : "^" + selected.Id);
                item.Set("Amount", amount);
                updated.Add(item);
            }
            _draft.Set("ProductionState", updated);
        }

        if (_buildingRows.Count > 0 || _activeTab == "Building States")
        {
            var arr = EnsureArray("BuildingStates", 48);
            for (int i = 0; i < _buildingRows.Count; i++)
                arr.Set(i, ParseInt(_buildingRows[i].Value.Text, $"Building state {i + 1}"));
        }
        CaptureEditorRaw();

        foreach (var date in _dates)
        {
            long seconds = date.Picker.SelectedDate is DateTime value
                ? new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Local)).ToUnixTimeSeconds()
                : 0;
            _draft.Set(date.Key, seconds);
        }

        if (_text.TryGetValue("Name", out var name)) _heading.Text = string.IsNullOrWhiteSpace(name.Text) ? $"Settlement {_selectedIndex + 1}" : name.Text;
        return true;
    }

    public bool Commit()
    {
        if (_draft == null) return true;
        if (!CaptureCurrent()) return false;
        if (_pending)
        {
            try
            {
                _session.ApplySettlement(_selectedIndex, _draft);
                _pending = false;
                _status(UiStrings.Get("settlement.changes_applied"));
                RefreshRecordList(_selectedIndex);
                _draft = _session.ReadSettlement(_selectedIndex);
                _heading.Text = ReadString(_draft, "Name", $"Settlement {_selectedIndex + 1}");
            }
            catch (Exception ex) { _error(ex); return false; }
        }
        return true;
    }

    void ApplyChanges()
    {
        if (!Commit()) return;
        ShowCurrent();
    }

    JsonArray EnsureArray(string key, int minLength = 0)
    {
        var arr = _draft!.GetArray(key);
        if (arr == null) { arr = new JsonArray(); _draft.Set(key, arr); }
        while (arr.Length < minLength) arr.Add(0);
        return arr;
    }

    JsonObject EnsureObject(string key)
    {
        var obj = _draft!.GetObject(key);
        if (obj == null) { obj = new JsonObject(); _draft.Set(key, obj); }
        return obj;
    }

    void DeleteSettlement(object sender, RoutedEventArgs e)
    {
        if (_selectedIndex < 0 || !Commit()) return;
        if (MessageBox.Show(UiStrings.Get("settlement.delete_prompt"), UiStrings.Get("settlement.delete_title"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            int prior = _selectedIndex;
            _session.DeleteSettlement(prior);
            _draft = null; _selectedIndex = -1; _pending = false;
            int next = Math.Min(prior, _session.SettlementCount - 1);
            RefreshRecordList(next);
            if (_records.SelectedItem is not SettlementEntry)
            {
                _heading.Text = "Select a settlement";
                ShowCurrent();
            }
            _status(UiStrings.Get("settlement.deleted"));
        }
        catch (Exception ex) { _error(ex); }
    }

    void ExportSettlement(object sender, RoutedEventArgs e)
    {
        if (_selectedIndex < 0 || !Commit() || _draft == null) return;
        var dialog = new SaveFileDialog { Filter = UiStrings.Get("settlement.file_filter"), DefaultExt = "stl", AddExtension = true, FileName = string.IsNullOrWhiteSpace(ReadString(_draft, "Name", "")) ? "settlement.stl" : ReadString(_draft, "Name", "settlement") + ".stl" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try { File.WriteAllText(dialog.FileName, _draft.ToFormattedString()); _status(UiStrings.Get("settlement.exported")); }
        catch (Exception ex) { _error(ex); }
    }

    void ImportSettlement(object sender, RoutedEventArgs e)
    {
        if (!Commit()) return;
        var dialog = new OpenFileDialog { Filter = UiStrings.Get("settlement.file_filter") };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            var imported = JsonObject.ImportFromFile(dialog.FileName);
            if (imported.GetObject("Data")?.GetObject("Settlement") is JsonObject wrapped) imported = wrapped.DeepClone();
            int? target = _selectedIndex >= 0 ? _selectedIndex : null;
            if (target.HasValue && MessageBox.Show(UiStrings.Format("settlement.replace_prompt", ReadString(_draft!, "Name", UiStrings.Get("settlement.selected"))), UiStrings.Get("settlement.import_title"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            int index = _session.ImportSettlement(imported, target);
            _selectedIndex = index;
            _draft = _session.ReadSettlement(index);
            _pending = false;
            RefreshRecordList(index);
            ShowCurrent();
            _status(UiStrings.Get("settlement.imported"));
        }
        catch (Exception ex) { _error(ex); }
    }

    static int ParseInt(string text, string label)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            throw new ArgumentException(UiStrings.Format("settlement.invalid_integer", UiStrings.LocalizeText(label)));
        return value;
    }

    static string ReadString(JsonObject? obj, string key, string fallback)
    {
        try { return obj?.GetString(key) ?? fallback; } catch { return fallback; }
    }

    static int ReadInt(JsonObject? obj, string key, int fallback)
    {
        try { return obj?.GetInt(key) ?? fallback; } catch { return fallback; }
    }

    static long ReadLong(JsonObject? obj, string key, long fallback)
    {
        try { return obj?.GetLong(key) ?? fallback; } catch { return fallback; }
    }

    static int ReadArrayInt(JsonArray? array, int index)
    {
        try { return array != null && index < array.Length ? array.GetInt(index) : 0; } catch { return 0; }
    }

    static string ReadArrayString(JsonArray? array, int index, string fallback)
    {
        try { return array != null && index < array.Length ? array.GetString(index) ?? fallback : fallback; } catch { return fallback; }
    }

    static Style StyleOf(string name) => (Style)Application.Current.FindResource(name);

    static StackPanel Inner(Border card) => (StackPanel)card.Child!;

    void UpdateBitPreview(TextBox raw, TextBlock classText, TextBlock stateText)
    {
        if (_binding) return;
        int value = ComposeBits();
        raw.Text = value.ToString(CultureInfo.InvariantCulture);
        var decoded = SettlementOperations.DecodeBuildingState(value);
        classText.Text = "Class: " + decoded.Class;
        stateText.Text = "State: " + decoded.State;
        MarkPending();
    }

    static Border EmptyState(string text)
    {
        var label = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 14, Margin = new Thickness(4, 8, 4, 8) };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Secondary");
        return new Border { Padding = new Thickness(14), CornerRadius = new CornerRadius(8), Background = (System.Windows.Media.Brush)Application.Current.FindResource("Surface"), Child = label };
    }
}