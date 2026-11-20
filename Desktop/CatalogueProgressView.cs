using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using NMSE.Next.Engine;
using NMSE.Data;

namespace NMSE.Next.Desktop;

internal sealed partial class DomainPageView
{
    private sealed record ProgressGridRow(CatalogueProgressRow Data, Func<ImageSource?> LoadImage)
    {
        public ImageSource? Icon => LoadImage();
        public string Category => UiStrings.LocalizeText(Data.Category);
        public string Name => System.Text.RegularExpressions.Regex.Match(Data.Name, @"^Slot (\d+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase) is { Success: true } match && int.TryParse(match.Groups[1].Value, out int slot) ? UiStrings.Format("settlement.slot_label", slot) : UiStrings.LocalizeText(Data.Name);
        public string Id => Data.Id;
        public string Progress => UiStrings.LocalizeText(Data.Progress);
        public string Target => UiStrings.LocalizeText(Data.Target);
        public string Status => UiStrings.LocalizeText(Data.Status);
        public string Detail => UiStrings.LocalizeText(Data.Detail);
    }

    private FrameworkElement BuildCatalogueProgress(string path)
    {
        var panel = new DockPanel();
        var filter = new TextBox { Width = 230, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "Filter entries" };
        var actions = new WrapPanel();
        var count = new TextBlock { Margin = new Thickness(6), VerticalAlignment = VerticalAlignment.Center };
        var grid = MakeCatalogueGrid(false);
        DockPanel.SetDock(filter, Dock.Top);
        DockPanel.SetDock(actions, Dock.Bottom);
        panel.Children.Add(filter);
        panel.Children.Add(actions);
        actions.Children.Add(count);
        panel.Children.Add(grid);

        void Column(string header, string property, int weight = 1) => grid.Columns.Add(new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(property),
            Width = new DataGridLength(weight, DataGridLengthUnitType.Star),
            MinWidth = 75
        });

        if (path is "Fossils" or "RawMaterials")
        {
            var icon = new FrameworkElementFactory(typeof(Image));
            icon.SetBinding(Image.SourceProperty, new Binding(nameof(ProgressGridRow.Icon)));
            icon.SetValue(HeightProperty, 22d);
            icon.SetValue(WidthProperty, 22d);
            grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = "Icon", Width = 48, CellTemplate = new DataTemplate { VisualTree = icon }
            });
        }

        if (path is "Wonders" or "CollectedKnowledge") Column("Category", nameof(CatalogueProgressRow.Category), 2);
        if (path != "DiscoveryStats") Column(path == "Wonders" ? "Slot" : "Name", nameof(CatalogueProgressRow.Name), 3);
        if (path is "Wonders") Column("Seed", nameof(CatalogueProgressRow.Detail), 4);
        if (path is not ("Wonders" or "CollectedKnowledge")) Column("ID", nameof(CatalogueProgressRow.Id), 2);
        if (path is "Wonders" or "CollectedKnowledge" or "DiscoveryStats")
            Column(path == "Wonders" ? "Value" : "Progress", nameof(CatalogueProgressRow.Progress));
        if (path is "CollectedKnowledge" or "DiscoveryStats") Column("Target", nameof(CatalogueProgressRow.Target));
        Column("Status", nameof(CatalogueProgressRow.Status));
        if (path == "CollectedKnowledge") Column("Details", nameof(CatalogueProgressRow.Detail), 4);

        CatalogueProgressRow[] rows = [];
        void Filter()
        {
            var query = filter.Text.Trim();
            grid.ItemsSource = rows.Where(row => new[] { row.Category, row.Name, row.Id, row.Progress, row.Target, row.Status, row.Detail }
                .Any(value => value.Contains(query, StringComparison.OrdinalIgnoreCase)))
                .Select(row => new ProgressGridRow(row, () => _owner.LoadIcon(row.Icon))).ToArray();
        }

        bool canEdit = _session.CanEditCatalogueProgress(path);
        void Refresh()
        {
            rows = _session.CatalogueProgress(path).ToArray();
            var known = rows.Count(row => row.Complete);
            
            count.Text = canEdit ? UiStrings.Format("discovery.known_count", known.ToString("N0"), rows.Length.ToString("N0")) : UiStrings.LocalizeText("Load an account file to view and edit discovery status.");
            Filter();
        }

        void Change(IEnumerable<CatalogueProgressRow> targets, bool complete)
        {
            var candidates = targets.Where(row => row.CanChange(complete)).ToArray();
            if (candidates.Length == 0) return;
            if (!ConfirmCatalogue($"{(complete ? "Complete" : "Clear")} {candidates.Length:N0} entries?")) return;
            if (_session.EditCatalogueProgress(path, candidates, complete) > 0)
                _owner.PageStatus("Catalogue updated in memory. Press Save to keep the changes.");
            Refresh();
        }

        CatalogueAction(actions, "Complete All", () => Change(rows, true)).IsEnabled = canEdit;
        CatalogueAction(actions, "Clear All", () => Change(rows, false)).IsEnabled = canEdit;
        var completeSelected = CatalogueAction(actions, "Complete Selected", () =>
            Change(grid.SelectedItems.Cast<ProgressGridRow>().Select(row => row.Data), true));
        var clearSelected = CatalogueAction(actions, "Clear Selected", () =>
            Change(grid.SelectedItems.Cast<ProgressGridRow>().Select(row => row.Data), false));

        void UpdateSelection()
        {
            var selected = grid.SelectedItems.Cast<ProgressGridRow>();
            completeSelected.IsEnabled = canEdit && selected.Any(row => row.Data.CanChange(true));
            clearSelected.IsEnabled = canEdit && selected.Any(row => row.Data.CanChange(false));
        }

        grid.SelectionChanged += (_, _) => UpdateSelection();
        filter.TextChanged += (_, _) => Filter();
        Refresh();
        UpdateSelection();
        return new Border { Style = PageStyle("Card"), Child = panel };
    }
}

