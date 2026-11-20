using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NMSE.Data;
using NMSE.Next.Engine;

namespace NMSE.Next.Desktop;

internal sealed class AccountRewardItem : INotifyPropertyChanged
{
    readonly bool _originalUnlocked, _originalRedeemed;
    bool _unlocked, _redeemed;
    public string Id { get; }
    public string Name { get; }
    public string Expedition { get; }
    public ImageSource? Icon { get; }
    public bool CanUnlock { get; }
    public bool CanRedeem { get; }
    public bool Unlocked { get => _unlocked; set { if (_unlocked == value) return; _unlocked = value; PropertyChanged?.Invoke(this, new(nameof(Unlocked))); PropertyChanged?.Invoke(this, new(nameof(Changed))); } }
    public bool Redeemed { get => _redeemed; set { if (_redeemed == value) return; _redeemed = value; PropertyChanged?.Invoke(this, new(nameof(Redeemed))); PropertyChanged?.Invoke(this, new(nameof(Changed))); } }
    public bool Changed => _unlocked != _originalUnlocked || _redeemed != _originalRedeemed;
    public event PropertyChangedEventHandler? PropertyChanged;

    public AccountRewardItem(AccountRewardRow row, ImageSource? icon, bool canUnlock, bool canRedeem)
    {
        Id = row.Id; Name = UiStrings.LocalizeText(row.Name); Expedition = row.Season < 0 ? "" : row.Season.ToString();
        Icon = icon; CanUnlock = canUnlock; CanRedeem = canRedeem;
        _unlocked = _originalUnlocked = row.Unlocked; _redeemed = _originalRedeemed = row.Redeemed;
    }
}

internal sealed class AccountRewardsPageView : Grid
{
    static readonly (string Key, string Title)[] Sections =
    [
        ("season", "Expedition rewards"), ("twitch", "Twitch rewards"), ("platform", "Platform rewards"),
        ("special", "Special unlocks"), ("redeemed-season", "Redeemed expedition rewards"),
        ("redeemed-twitch", "Redeemed Twitch rewards"), ("redeemed-platform", "Redeemed platform rewards")
    ];

    readonly MainWindow _owner;
    readonly SaveSession _session;
    readonly WrapPanel _tabs = new();
    readonly DataGrid _grid = new();
    readonly TextBox _filter = new() { Width = 210, Margin = new Thickness(4) };
    readonly TextBlock _count = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 7, 0) };
    readonly TextBlock _accountNote = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(5, 3, 5, 6) };
    readonly List<AccountRewardItem> _allRows = [];
    readonly List<Button> _tabButtons = [];
    string _section = "season";
    string _query = "";

    internal bool HasPending => _allRows.Any(x => x.Changed);

    public AccountRewardsPageView(MainWindow owner, SaveSession session)
    {
        _owner = owner; _session = session;
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new() { Height = GridLength.Auto });

        var heading = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 2, 4, 6) };
        heading.Children.Add(ConceptIcons.Make("Account Rewards", 25));
        heading.Children.Add(new TextBlock { Text = "Account Rewards", Style = StyleOf("Heading"), Margin = new Thickness(9, 0, 0, 0) });
        Children.Add(heading);

        var tabScroll = new ScrollViewer { Content = _tabs, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 88 };
        SetRow(tabScroll, 1); Children.Add(tabScroll);
        foreach (var section in Sections)
        {
            var button = new Button { Content = section.Title, Tag = section.Key, Margin = new Thickness(3, 3, 3, 3), Padding = new Thickness(10, 6, 10, 6) };
            button.Click += (_, _) => { if (Commit()) { _section = section.Key; Render(); } };
            _tabButtons.Add(button); _tabs.Children.Add(button);
        }

        var warning = new TextBlock
        {
            Text = "Twitch drops and platform rewards require you to be offline before starting the game. Claim them at the Synthesis vendor in the Anomaly.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 4, 6, 4)
        };
        SetRow(warning, 2); Children.Add(warning);

        var toolbar = new DockPanel { LastChildFill = true, Margin = new Thickness(2, 2, 2, 4) };
        var commands = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        AddToolbarButton(commands, "Check Consistency…", CheckConsistency);
        AddToolbarButton(commands, "Unlock All", () => SetVisible(unlocked: true));
        AddToolbarButton(commands, "Lock All", () => SetVisible(unlocked: false));
        AddToolbarButton(commands, "Redeem All", () => SetVisible(redeemed: true));
        AddToolbarButton(commands, "Remove All", () => SetVisible(unlocked: false, redeemed: false));
        DockPanel.SetDock(commands, Dock.Left); toolbar.Children.Add(commands);
        var filterPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        filterPanel.Children.Add(new TextBlock { Text = "Filter:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 2, 0) });
        _filter.ToolTip = "Filter by reward ID or name"; _filter.TextChanged += (_, _) => { _query = _filter.Text.Trim(); FilterRows(); };
        filterPanel.Children.Add(_filter);
        var clear = new Button { Content = "×", MinWidth = 28, Margin = new Thickness(2) }; clear.Click += (_, _) => _filter.Clear(); filterPanel.Children.Add(clear);
        DockPanel.SetDock(filterPanel, Dock.Right); toolbar.Children.Add(filterPanel);
        SetRow(toolbar, 3); Children.Add(toolbar);

        ConfigureGrid();
        SetRow(_grid, 4); Children.Add(_grid);
        var bottom = new DockPanel { Margin = new Thickness(4, 6, 4, 2) };
        _count.SetResourceReference(TextBlock.ForegroundProperty, "Secondary"); bottom.Children.Add(_count);
        var apply = new Button { Content = "✓ Apply changes", Style = StyleOf("Primary"), HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(14, 6, 14, 6) };
        apply.Click += (_, _) => Commit(); DockPanel.SetDock(apply, Dock.Right); bottom.Children.Add(apply);
        SetRow(bottom, 5); Children.Add(bottom);
        Render();
    }

    internal bool Commit()
    {
        var changed = _allRows.Where(x => x.Changed).Select(x => new AccountRewardEdit(x.Id, x.Unlocked, x.Redeemed)).ToArray();
        if (changed.Length == 0) return true;
        try
        {
            _session.ApplyAccountRewardEdits(_section, changed);
            _owner.PageStatus("Account reward changes applied in memory. Press Save to keep them.");
            Render();
            return true;
        }
        catch (Exception ex) { _owner.PageError(ex); return false; }
    }

    void Render()
    {
        foreach (var button in _tabButtons)
        {
            bool selected = Equals(button.Tag, _section);
            button.SetResourceReference(Control.BackgroundProperty, selected ? "AccentSurface" : "Surface");
            button.SetResourceReference(Control.ForegroundProperty, selected ? "OnAccent" : "Text");
        }

        _allRows.Clear();
        bool accountSection = _section is "season" or "twitch" or "platform" or "special";
        bool redeemedSection = _section.StartsWith("redeemed-", StringComparison.Ordinal);
        bool canUnlock = accountSection && _session.AccountData != null;
        bool canRedeem = !(_section == "special");
        foreach (var row in _session.ReadAccountRewardRows(_section))
            _allRows.Add(new(row, _owner.LoadIcon(row.Icon), canUnlock, canRedeem));
        _accountNote.Text = _session.AccountData == null
            ? (_session.AccountLoadMessage ?? "Account data is unavailable. Open a save with accountdata.hg to edit account unlocks.")
            : $"Account data: {_allRows.Count(r => r.Unlocked):N0} rewards unlocked";
        _accountNote.Visibility = accountSection ? Visibility.Visible : Visibility.Collapsed;
        _accountNote.SetResourceReference(TextBlock.ForegroundProperty, _session.AccountData == null ? "Accent" : "Secondary");

        // Actions are scoped to the selected reward group. Redeemed-only sections keep
        // their list focused on save state; account-only tabs still expose both columns.
        foreach (var button in _tabs.Children.OfType<Button>()) button.IsEnabled = true;
        _grid.Columns[4].Visibility = accountSection ? Visibility.Visible : Visibility.Collapsed;
        _grid.Columns[4].IsReadOnly = _session.AccountData == null;
        _grid.Columns[5].Visibility = canRedeem ? Visibility.Visible : Visibility.Collapsed;
        if (redeemedSection) _grid.Columns[4].Visibility = Visibility.Collapsed;
        _grid.ItemsSource = null;
        _query = _filter.Text.Trim();
        FilterRows();
    }

    void FilterRows()
    {
        var rows = _allRows.Where(r => _query.Length == 0 ||
            r.Id.Contains(_query, StringComparison.OrdinalIgnoreCase) ||
            r.Name.Contains(_query, StringComparison.OrdinalIgnoreCase) ||
            r.Expedition.Contains(_query, StringComparison.OrdinalIgnoreCase)).ToArray();
        _grid.ItemsSource = rows;
        _count.Text = UiStrings.Format("ui.account.records", rows.Length.ToString("N0"), _allRows.Count.ToString("N0"));
    }

    void SetVisible(bool? unlocked = null, bool? redeemed = null)
    {
        foreach (var row in _allRows.Where(r => _query.Length == 0 ||
                     r.Id.Contains(_query, StringComparison.OrdinalIgnoreCase) ||
                     r.Name.Contains(_query, StringComparison.OrdinalIgnoreCase) ||
                     r.Expedition.Contains(_query, StringComparison.OrdinalIgnoreCase)))
        {
            if (unlocked.HasValue && row.CanUnlock) row.Unlocked = unlocked.Value;
            if (redeemed.HasValue && row.CanRedeem) row.Redeemed = redeemed.Value;
        }
    }

    void CheckConsistency()
    {
        try
        {
            var issues = _session.CheckAccountRewardConsistency().ToList();
            if (issues.Count == 0) { _owner.PageStatus("Reward arrays are consistent."); MessageBox.Show(Window.GetWindow(this), UiStrings.LocalizeText("No reward consistency issues were found."), UiStrings.LocalizeText("Check Consistency…").TrimEnd('…'), MessageBoxButton.OK, MessageBoxImage.Information); return; }
            var dialog = new AccountRewardConsistencyWindow(_session, issues) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true && dialog.HasChanges)
                _owner.PageStatus("Consistency changes applied in memory. Press Save to keep them.");
        }
        catch (Exception ex) { _owner.PageError(ex); }
    }

    void ConfigureGrid()
    {
        _grid.AutoGenerateColumns = false; _grid.CanUserAddRows = false; _grid.CanUserDeleteRows = false; _grid.IsReadOnly = false;
        _grid.SelectionMode = DataGridSelectionMode.Extended; _grid.HeadersVisibility = DataGridHeadersVisibility.Column;
        _grid.RowHeight = 32; _grid.MinHeight = 180; _grid.GridLinesVisibility = DataGridGridLinesVisibility.All;
        _grid.SetResourceReference(Control.BackgroundProperty, "Input"); _grid.SetResourceReference(Control.ForegroundProperty, "Text"); _grid.SetResourceReference(Control.BorderBrushProperty, "Edge");
        _grid.Columns.Add(new DataGridTemplateColumn { Header = "", Width = 38, IsReadOnly = true, CellTemplate = ImageTemplate() });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Reward ID", Binding = new Binding(nameof(AccountRewardItem.Id)), Width = new DataGridLength(1.15, DataGridLengthUnitType.Star), IsReadOnly = true });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new Binding(nameof(AccountRewardItem.Name)), Width = new DataGridLength(2.1, DataGridLengthUnitType.Star), IsReadOnly = true });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Expedition", Binding = new Binding(nameof(AccountRewardItem.Expedition)), Width = new DataGridLength(0.65, DataGridLengthUnitType.Star), IsReadOnly = true });
        _grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Unlocked on Account", Binding = new Binding(nameof(AccountRewardItem.Unlocked)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Redeemed in Save", Binding = new Binding(nameof(AccountRewardItem.Redeemed)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
    }

    static DataTemplate ImageTemplate()
    {
        var image = new FrameworkElementFactory(typeof(Image));
        image.SetValue(FrameworkElement.WidthProperty, 24d); image.SetValue(FrameworkElement.HeightProperty, 24d);
        image.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        image.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        image.SetBinding(Image.SourceProperty, new Binding(nameof(AccountRewardItem.Icon)));
        return new DataTemplate { VisualTree = image };
    }

    void AddToolbarButton(Panel panel, string text, Action click)
    {
        var button = new Button { Content = text, Margin = new Thickness(3), Padding = new Thickness(9, 5, 9, 5) };
        button.Click += (_, _) => { if (!_owner.IsBusy) click(); };
        panel.Children.Add(button);
    }

    static Style StyleOf(string name) => (Style)Application.Current.FindResource(name);
}

internal sealed class AccountRewardConsistencyWindow : Window
{
    readonly SaveSession _session;
    readonly ListBox _issues = new();
    readonly TextBlock _count = new() { Margin = new Thickness(6, 4, 6, 4) };
    List<AccountRewardConsistencyIssue> _rows;
    public bool HasChanges { get; private set; }

    public AccountRewardConsistencyWindow(SaveSession session, List<AccountRewardConsistencyIssue> rows)
    {
        _session = session; _rows = rows;
        Title = "Check Consistency"; Width = 900; Height = 510; MinWidth = 650; MinHeight = 340;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "Surface"); SetResourceReference(ForegroundProperty, "Text");
        var layout = new DockPanel { Margin = new Thickness(10) };
        var top = new StackPanel();
        var commands = new WrapPanel();
        var add = new Button { Content = "Add All to Missing Arrays", Margin = new Thickness(3), Padding = new Thickness(9, 5, 9, 5) };
        add.Click += (_, _) => ResolveAll(true);
        var remove = new Button { Content = "Remove All from Current Arrays", Margin = new Thickness(3), Padding = new Thickness(9, 5, 9, 5) };
        remove.Click += (_, _) => ResolveAll(false);
        commands.Children.Add(add); commands.Children.Add(remove); top.Children.Add(commands);
        top.Children.Add(_count); _count.SetResourceReference(TextBlock.ForegroundProperty, "Secondary");
        top.Children.Add(new TextBlock { Text = "Select an issue, then choose whether to add it to the missing array or remove it from the current array.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 2, 6, 8) });
        DockPanel.SetDock(top, Dock.Top); layout.Children.Add(top);

        _issues.SetResourceReference(Control.BackgroundProperty, "Input"); _issues.SetResourceReference(Control.ForegroundProperty, "Text");
        _issues.SelectionChanged += (_, _) => UpdateActions();
        DockPanel.SetDock(_issues, Dock.Top); layout.Children.Add(_issues);

        var bottom = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(2, 6, 2, 0) };
        var addSelected = new Button { Content = "Add selected to missing", Margin = new Thickness(3), Padding = new Thickness(9, 5, 9, 5) };
        addSelected.Click += (_, _) => ResolveSelected(true);
        var removeSelected = new Button { Content = "Remove selected from current", Margin = new Thickness(3), Padding = new Thickness(9, 5, 9, 5) };
        removeSelected.Click += (_, _) => ResolveSelected(false);
        var close = new Button { Content = "Close", IsDefault = true, Margin = new Thickness(3), Padding = new Thickness(9, 5, 9, 5) };
        close.Click += (_, _) => { DialogResult = true; Close(); };
        bottom.Children.Add(addSelected); bottom.Children.Add(removeSelected); bottom.Children.Add(close);
        DockPanel.SetDock(bottom, Dock.Bottom); layout.Children.Add(bottom);
        Content = layout;
        Render();
    }

    void Render()
    {
        _count.Text = $"{_rows.Count:N0} inconsistency issue(s) found";
        _issues.ItemsSource = _rows.Select(x => $"{x.Name} · {x.Id} — {x.Description}").ToArray();
        UpdateActions();
    }
    void UpdateActions()
    {
        bool selected = _issues.SelectedIndex >= 0 && _issues.SelectedIndex < _rows.Count;
        if (Content is DockPanel root && root.Children.OfType<WrapPanel>().LastOrDefault() is {} actions)
            foreach (var b in actions.Children.OfType<Button>().Take(2)) b.IsEnabled = selected;
    }
    void ResolveSelected(bool add)
    {
        int index = _issues.SelectedIndex;
        if (index < 0 || index >= _rows.Count) return;
        _session.ResolveAccountRewardConsistency(_rows[index], add); HasChanges = true; _rows = _session.CheckAccountRewardConsistency().ToList(); Render();
    }
    void ResolveAll(bool add)
    {
        foreach (var issue in _rows.ToArray()) _session.ResolveAccountRewardConsistency(issue, add);
        HasChanges |= _rows.Count > 0; _rows = _session.CheckAccountRewardConsistency().ToList(); Render();
    }
}
