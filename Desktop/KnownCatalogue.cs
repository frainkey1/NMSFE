using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using NMSE.Data;
using NMSE.Next.Engine;

namespace NMSE.Next.Desktop;

internal sealed partial class DomainPageView
{
    private sealed record CatalogueRow(string Id, string Name, string Category, Func<ImageSource?> LoadImage)
    {
        public ImageSource? Icon => LoadImage();
    }

    private CatalogueRow MakeCatalogueRow(string id, GameItem? item) =>
        new(id, item?.Name ?? id, item?.ItemType ?? "", () => _owner.LoadIcon(item?.Icon));

    private static bool CatalogueMatches(CatalogueRow row, string query) =>
        row.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
        || row.Category.Contains(query, StringComparison.OrdinalIgnoreCase)
        || row.Id.Contains(query, StringComparison.OrdinalIgnoreCase);

    private const string DiscoveryFilter = "Discovery files (*.nmsdiscover)|*.nmsdiscover|JSON files (*.json)|*.json|All files (*.*)|*.*";

    private DataGrid MakeCatalogueGrid(bool includeItemColumns = true)
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Extended,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            EnableRowVirtualization = true
        };
        grid.SetResourceReference(Control.BackgroundProperty, "Surface");
        grid.SetResourceReference(Control.ForegroundProperty, "Text");
        grid.SetResourceReference(DataGrid.RowBackgroundProperty, "Input");
        grid.SetResourceReference(DataGrid.HorizontalGridLinesBrushProperty, "Edge");
        var header = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
        header.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("Surface")));
        header.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Text")));
        header.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6)));
        grid.ColumnHeaderStyle = header;
        if (!includeItemColumns) return grid;

        var image = new FrameworkElementFactory(typeof(Image));
        image.SetBinding(Image.SourceProperty, new Binding(nameof(CatalogueRow.Icon)));
        image.SetValue(WidthProperty, 20d);
        image.SetValue(HeightProperty, 20d);
        image.SetValue(MarginProperty, new Thickness(3));
        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Icon", Width = 46, CellTemplate = new DataTemplate { VisualTree = image }
        });
        foreach (string column in new[] { "Name", "Category", "Id" })
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = column == "Id" ? "ID" : column,
                Binding = new Binding(column),
                Width = new DataGridLength(column == "Name" ? 2 : 1, DataGridLengthUnitType.Star),
                MinWidth = 90
            });
        return grid;
    }

    private FrameworkElement BuildKnownCatalogue(string path)
    {
        var panel = new DockPanel();
        var search = new TextBox { ToolTip = "Filter by name, category, or ID", Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(search, Dock.Top);
        panel.Children.Add(search);
        var actions = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        DockPanel.SetDock(actions, Dock.Bottom);
        panel.Children.Add(actions);
        var count = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6) };
        actions.Children.Add(count);
        var grid = MakeCatalogueGrid();
        panel.Children.Add(grid);

        var rows = Array.Empty<CatalogueRow>();
        void Filter()
        {
            var query = search.Text.Trim();
            grid.ItemsSource = rows.Where(row => CatalogueMatches(row, query)).ToArray();
        }

        void Refresh()
        {
            rows = _session.KnownCatalogueIds(path).Select(id => MakeCatalogueRow(id, _session.Items.GetItem(id))).ToArray();
            var (have, total) = _session.KnownCatalogueCompletion(path);
            count.Text = UiStrings.Format("discovery.known_count", have.ToString("N0"), total.ToString("N0"));
            Filter();
        }

        void Change(Func<int> update, string verb)
        {
            int changed = update();
            Refresh();
            _owner.PageStatus(changed == 0 ? "No catalogue changes." :
                $"{verb} {changed:N0} catalogue entries in memory. Press Save to keep the changes.");
        }

        Button Action(string label, Action run)
        {
            var button = new Button { Content = label };
            button.Click += (_, _) =>
            {
                try { run(); }
                catch (Exception ex) { _owner.PageError(ex); }
            };
            actions.Children.Add(button);
            return button;
        }

        Action("Add all missing", () =>
        {
            if (MessageBox.Show(_owner, UiStrings.LocalizeText("Add all missing catalogue entries for this tab?"), UiStrings.LocalizeText("Add all missing"),
                    MessageBoxButton.OKCancel) == MessageBoxResult.OK)
                Change(() => _session.AddMissingCatalogue(path), "Added");
        });
        string addLabel = path switch
        {
            "KnownTech" => "Add technology",
            "KnownProducts" => "Add product",
            _ => "Add special"
        };
        Action(addLabel, () =>
        {
            var selected = PickCatalogueItems(path, addLabel);
            if (selected.Length > 0) Change(() => _session.AddKnownCatalogueIds(path, selected), "Added");
        });
        var remove = Action("Remove selected", () =>
        {
            var ids = grid.SelectedItems.Cast<CatalogueRow>().Select(row => row.Id).ToArray();
            Change(() => _session.RemoveKnownCatalogueIds(path, ids), "Removed");
        });
        remove.IsEnabled = false;
        grid.SelectionChanged += (_, _) => remove.IsEnabled = grid.SelectedItems.Count > 0;

        string key = SaveSession.KnownCatalogueExportKey(path);
        Action("Export…", () =>
        {
            var dialog = new SaveFileDialog { Filter = UiStrings.LocalizeText(DiscoveryFilter), DefaultExt = ".nmsdiscover", FileName = key };
            if (dialog.ShowDialog(_owner) != true) return;
            var data = new Dictionary<string, string[]> { [key] = _session.KnownCatalogueIds(path) };
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            _owner.PageStatus("Catalogue exported.");
        });
        Action("Import…", () =>
        {
            var dialog = new OpenFileDialog { Filter = UiStrings.LocalizeText(DiscoveryFilter) };
            if (dialog.ShowDialog(_owner) != true) return;
            using var data = JsonDocument.Parse(File.ReadAllText(dialog.FileName));
            if (data.RootElement.ValueKind != JsonValueKind.Object
                || (!data.RootElement.TryGetProperty(key, out var list) && !data.RootElement.TryGetProperty(path, out list))
                || list.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException($"Choose a {key} discovery file.");
            var ids = list.EnumerateArray().Select(value => value.GetString() ?? "").ToArray();
            Change(() => _session.AddKnownCatalogueIds(path, ids), "Imported");
        });
        search.TextChanged += (_, _) => Filter();
        Refresh();
        return new Border { Style = PageStyle("Card"), Child = panel };
    }

    private string[] PickCatalogueItems(string path, string title) =>
        PickCatalogueRows(title, _session.CatalogueChoices(path).OrderBy(item => item.Name)
            .Select(item => MakeCatalogueRow(item.Id.TrimStart('^'), item)).ToArray());

    private string[] PickCatalogueRows(string title, CatalogueRow[] rows)
    {
        var window = new Window
        {
            Owner = _owner, Title = title, Width = 660, Height = 500,
            MinWidth = 360, MinHeight = 260, WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        window.SetResourceReference(Control.BackgroundProperty, "Surface");
        window.SetResourceReference(Control.ForegroundProperty, "Text");

        var panel = new DockPanel { Margin = new Thickness(12) };
        var search = new TextBox { ToolTip = "Filter by name, category, or ID" };
        DockPanel.SetDock(search, Dock.Top);
        panel.Children.Add(search);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(actions, Dock.Bottom);
        panel.Children.Add(actions);
        var add = new Button { Content = "Add selected", IsDefault = true, IsEnabled = false };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        actions.Children.Add(add);
        actions.Children.Add(cancel);
        var grid = MakeCatalogueGrid();
        panel.Children.Add(grid);
        grid.ItemsSource = rows;
        search.TextChanged += (_, _) => grid.ItemsSource = rows.Where(row => CatalogueMatches(row, search.Text.Trim())).ToArray();
        grid.SelectionChanged += (_, _) => add.IsEnabled = grid.SelectedItems.Count > 0;
        add.Click += (_, _) => window.DialogResult = true;
        window.Content = panel;
        window.Loaded += (_, _) => search.Focus();
        return window.ShowDialog() == true ? grid.SelectedItems.Cast<CatalogueRow>().Select(row => row.Id).ToArray() : [];
    }
}