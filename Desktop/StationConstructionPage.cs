using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using NMSE.Data;
using NMSE.Next.Engine;

namespace NMSE.Next.Desktop;

internal sealed class StationDecorationRow : INotifyPropertyChanged
{
    readonly bool _originalUnlocked;
    bool _unlocked;

    public string Id { get; }
    public string Name { get; }
    public ImageSource? Icon { get; }
    public bool Unlocked
    {
        get => _unlocked;
        set
        {
            if (_unlocked == value) return;
            _unlocked = value;
            PropertyChanged?.Invoke(this, new(nameof(Unlocked)));
            PropertyChanged?.Invoke(this, new(nameof(Changed)));
        }
    }
    public bool Changed => _unlocked != _originalUnlocked;
    public event PropertyChangedEventHandler? PropertyChanged;

    public StationDecorationRow(GameItem item, ImageSource? icon, bool unlocked)
    {
        Id = item.Id;
        Name = item.Name;
        Icon = icon;
        _unlocked = _originalUnlocked = unlocked;
    }
}

internal sealed partial class DomainPageView
{
    private FrameworkElement BuildStationConstruction()
    {
        var panel = new Grid { Margin = new Thickness(8) };
        panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        List<StationDecorationRow> rows = [];
        string query = "";

        var heading = new TextBlock
        {
            Text = "Exterior decorations",
            Style = PageStyle("Heading"),
            Margin = new Thickness(4, 0, 4, 6)
        };
        panel.Children.Add(heading);

        var toolbar = new DockPanel { Margin = new Thickness(2, 2, 2, 4) };
        var commands = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        AddToolbarButton(commands, "Check Consistency…", CheckConsistency);
        AddToolbarButton(commands, "Unlock All", () => SetVisible(unlocked: true));
        AddToolbarButton(commands, "Lock All", () => SetVisible(unlocked: false));
        DockPanel.SetDock(commands, Dock.Left);
        toolbar.Children.Add(commands);

        var filterPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        filterPanel.Children.Add(new TextBlock
        {
            Text = "Filter:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 2, 0)
        });
        var filter = new TextBox { Width = 210, Margin = new Thickness(4), ToolTip = "Filter by decoration ID or name" };
        filterPanel.Children.Add(filter);
        var clear = new Button { Content = "×", MinWidth = 28, Margin = new Thickness(2) };
        clear.Click += (_, _) => filter.Clear();
        filterPanel.Children.Add(clear);
        DockPanel.SetDock(filterPanel, Dock.Right);
        toolbar.Children.Add(filterPanel);
        Grid.SetRow(toolbar, 1);
        panel.Children.Add(toolbar);

        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            IsReadOnly = false,
            SelectionMode = DataGridSelectionMode.Extended,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            RowHeight = 32,
            MinHeight = 180,
            GridLinesVisibility = DataGridGridLinesVisibility.All
        };
        grid.SetResourceReference(Control.BackgroundProperty, "Input");
        grid.SetResourceReference(Control.ForegroundProperty, "Text");
        grid.SetResourceReference(Control.BorderBrushProperty, "Edge");
        grid.Columns.Add(new DataGridTemplateColumn { Header = "", Width = 38, IsReadOnly = true, CellTemplate = IconTemplate() });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Item ID",
            Binding = new Binding(nameof(StationDecorationRow.Id)),
            Width = new DataGridLength(1.15, DataGridLengthUnitType.Star),
            IsReadOnly = true
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Name",
            Binding = new Binding(nameof(StationDecorationRow.Name)),
            Width = new DataGridLength(2.1, DataGridLengthUnitType.Star),
            IsReadOnly = true
        });
        grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "Unlocked",
            Binding = new Binding(nameof(StationDecorationRow.Unlocked))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        Grid.SetRow(grid, 2);
        panel.Children.Add(grid);

        rows = LoadRows();
        var count = new TextBlock { Margin = new Thickness(7, 5, 7, 2), VerticalAlignment = VerticalAlignment.Center };
        count.SetResourceReference(TextBlock.ForegroundProperty, "Secondary");

        var bottom = new DockPanel { Margin = new Thickness(4, 6, 4, 2) };
        bottom.Children.Add(count);
        var apply = new Button
        {
            Content = "✓ Apply changes",
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(14, 6, 14, 6)
        };
        apply.SetResourceReference(Control.StyleProperty, "Primary");
        apply.Click += (_, _) => Commit();
        DockPanel.SetDock(apply, Dock.Right);
        bottom.Children.Add(apply);
        Grid.SetRow(bottom, 3);
        panel.Children.Add(bottom);

        void Refresh()
        {
            query = filter.Text.Trim();
            var visible = rows.Where(row => query.Length == 0
                    || row.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || row.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            grid.ItemsSource = visible;
            int unlocked = rows.Count(row => row.Unlocked);
            count.Text = $"{visible.Length:N0} / {rows.Count:N0} decorations · {unlocked:N0} unlocked";
        }

        void SetVisible(bool unlocked)
        {
            foreach (var row in rows.Where(row => query.Length == 0
                         || row.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
                         || row.Name.Contains(query, StringComparison.OrdinalIgnoreCase)))
                row.Unlocked = unlocked;
        }

        void Commit()
        {
            var changed = rows.Where(row => row.Changed).ToArray();
            if (changed.Length == 0) return;
            try
            {
                var unlock = changed.Where(row => row.Unlocked).Select(row => row.Id).ToArray();
                var lockIds = changed.Where(row => !row.Unlocked).Select(row => row.Id).ToArray();
                if (unlock.Length > 0) _session.AddKnownCatalogueIds("KnownProducts", unlock);
                if (lockIds.Length > 0) _session.RemoveKnownCatalogueIds("KnownProducts", lockIds);
                _owner.PageStatus("Station decoration changes applied in memory. Press Save to keep them.");
                rows = LoadRows();
                Refresh();
            }
            catch (Exception ex) { _owner.PageError(ex); }
        }

        void CheckConsistency()
        {
            try
            {
                var issues = _session.CheckAccountRewardConsistency().ToList();
                if (issues.Count == 0)
                {
                    _owner.PageStatus("Reward arrays are consistent.");
                    MessageBox.Show(Window.GetWindow(panel), UiStrings.LocalizeText("No reward consistency issues were found."),
                        UiStrings.LocalizeText("Check Consistency"), MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var dialog = new AccountRewardConsistencyWindow(_session, issues) { Owner = Window.GetWindow(panel) };
                if (dialog.ShowDialog() == true && dialog.HasChanges)
                    _owner.PageStatus("Consistency changes applied in memory. Press Save to keep them.");
            }
            catch (Exception ex) { _owner.PageError(ex); }
        }

        List<StationDecorationRow> LoadRows()
        {
            const string group = "Space Station Decoration";
            var known = _session.KnownCatalogueIds("KnownProducts")
                .Select(Normalize)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return _session.Items.Items.Values
                .Where(item => item.ItemType.Equals("Station", StringComparison.OrdinalIgnoreCase)
                    && item.Subtitle.Equals(group, StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .Select(item => new StationDecorationRow(item, _owner.LoadIcon(item.Icon), known.Contains(Normalize(item.Id))))
                .ToList();
        }

        static string Normalize(string id) => id.Trim().TrimStart('^');

        static DataTemplate IconTemplate()
        {
            var image = new FrameworkElementFactory(typeof(Image));
            image.SetValue(FrameworkElement.WidthProperty, 24d);
            image.SetValue(FrameworkElement.HeightProperty, 24d);
            image.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            image.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            image.SetBinding(Image.SourceProperty, new Binding(nameof(StationDecorationRow.Icon)));
            return new DataTemplate { VisualTree = image };
        }

        void AddToolbarButton(Panel target, string text, Action click)
        {
            var button = new Button { Content = text, Margin = new Thickness(3), Padding = new Thickness(9, 5, 9, 5) };
            button.Click += (_, _) => { if (!_owner.IsBusy) click(); };
            target.Children.Add(button);
        }

        filter.TextChanged += (_, _) => Refresh();
        Refresh();
        return new Border { Style = PageStyle("Card"), Child = panel };
    }
}
