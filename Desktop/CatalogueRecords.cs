using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using NMSE.Core;
using NMSE.Core.Utilities;
using NMSE.Data;
using NMSE.Models;
using NMSE.Next.Engine;

namespace NMSE.Next.Desktop;

internal sealed partial class DomainPageView
{
    private sealed record RecordRow(int Index, string Id, string Name, string Detail, string Extra,
        string Portal = "", string Decimal = "", string Booster = "", Func<ImageSource?>? LoadImage = null)
    {
        public ImageSource? Icon => LoadImage?.Invoke();
    }

    private FrameworkElement BuildCatalogueRecords(string path)
    {
        if (path == "KnownPortalRunes") return BuildKnownGlyphs();
        bool fishing = path == "FishingRecord";
        bool recipes = path == "KnownRefinerRecipes";
        bool recipeInfo = false;
        var panel = new DockPanel();
        var toolbar = new WrapPanel();
        var footer = new WrapPanel();
        var glyphs = new WrapPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        DockPanel.SetDock(glyphs, Dock.Bottom);
        panel.Children.Add(toolbar);
        panel.Children.Add(footer);
        panel.Children.Add(glyphs);
        var filter = new TextBox { Width = 230, ToolTip = "Filter by name, ID, or details" };
        toolbar.Children.Add(filter);
        var count = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6) };
        footer.Children.Add(count);
        var grid = MakeCatalogueGrid(false);
        panel.Children.Add(grid);

        if (fishing || recipes)
        {
            var image = new FrameworkElementFactory(typeof(Image));
            image.SetBinding(Image.SourceProperty, new Binding(nameof(RecordRow.Icon)));
            image.SetValue(WidthProperty, 22d);
            image.SetValue(HeightProperty, 22d);
            grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = "Icon", Width = 48, CellTemplate = new DataTemplate { VisualTree = image }
            });
        }

        void Column(string header, string property, int weight = 1) =>
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = header, Binding = new Binding(property), MinWidth = 75,
                Width = new DataGridLength(weight, DataGridLengthUnitType.Star)
            });

        if (fishing)
        {
            Column("Caught Fish", nameof(RecordRow.Id), 2);
            Column("Name", nameof(RecordRow.Name), 2);
            Column("Count", nameof(RecordRow.Detail));
            Column("Largest Catch", nameof(RecordRow.Extra));
        }
        else if (recipes)
        {
            Column("Name", nameof(RecordRow.Name), 2);
            Column("Type", nameof(RecordRow.Detail));
            Column("Result", nameof(RecordRow.Extra), 2);
            Column("Ingredients", nameof(RecordRow.Portal), 3);
            Column("ID", nameof(RecordRow.Id), 2);
        }
        else
        {
            Column("#", nameof(RecordRow.Index));
            Column("Name", nameof(RecordRow.Name), 3);
            Column("Type", nameof(RecordRow.Id));
            Column("Galaxy", nameof(RecordRow.Detail), 2);
            Column("Portal Code (Hex)", nameof(RecordRow.Portal), 2);
            Column("Portal Code (Dec)", nameof(RecordRow.Decimal), 2);
            Column("Signal Booster", nameof(RecordRow.Booster), 2);
        }

        RecordRow[] rows = [];
        var editActions = new List<Button>();
        void Filter()
        {
            int? previousIndex = (grid.SelectedItem as RecordRow)?.Index;
            var visible = rows.Where(row => new[] { row.Name, row.Id, row.Detail, row.Extra, row.Portal, row.Decimal, row.Booster }
                .Any(value => value.Contains(filter.Text.Trim(), StringComparison.OrdinalIgnoreCase))).ToArray();
            grid.ItemsSource = visible;
            if (!recipes && !fishing)
                grid.SelectedItem = visible.FirstOrDefault(row => row.Index == previousIndex) ?? visible.FirstOrDefault();
        }
        void Refresh()
        {
            rows = fishing ? FishingRows().ToArray() : recipes ? RecipeRows(recipeInfo).ToArray() : TeleportRows().ToArray();
            var (have, total) = fishing ? _session.FishingCompletion :
                (_session.KnownCatalogueIds(path).Length, _session.RecipeCompletionIds.Count);
            count.Text = fishing || recipes && !recipeInfo ? UiStrings.Format("discovery.known_count", have.ToString("N0"), total.ToString("N0"))
                : UiStrings.Format(recipes ? "discovery.recipe_count" : "discovery.destination_count", rows.Length.ToString("N0"));
            foreach (var button in editActions) button.Visibility = recipeInfo ? Visibility.Collapsed : Visibility.Visible;
            Filter();
        }

        void Changed()
        {
            Refresh();
            _owner.PageStatus("Catalogue updated in memory. Press Save to keep the changes.");
        }

        if (recipes)
        {
            CatalogueAction(toolbar, "Known Recipes", () => { recipeInfo = false; Refresh(); });
            CatalogueAction(toolbar, "Recipe Info", () => { recipeInfo = true; Refresh(); });
        }

        if (fishing || recipes)
        {
            editActions.Add(CatalogueAction(footer, "Add All Missing", () =>
            {
                if (!ConfirmCatalogue("Add all missing entries?")) return;
                if (fishing) _session.AddMissingFishing();
                else _session.AddKnownCatalogueIds(path, _session.RecipeCompletionIds);
                Changed();
            }));
            editActions.Add(CatalogueAction(footer, fishing ? "Add Fish…" : "Add Recipe…", () =>
            {
                var known = rows.Select(row => row.Id.TrimStart('^')).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var options = fishing
                    ? _session.Items.Items.Values.Where(item => item.ItemType.Equals("Fish", StringComparison.OrdinalIgnoreCase))
                        .Select(item => MakeCatalogueRow(item.Id.TrimStart('^'), item))
                    : _session.Recipes.Select(recipe => new CatalogueRow(recipe.Id.TrimStart('^'), recipe.RecipeName,
                        recipe.RecipeType, () => _owner.LoadIcon(_session.Items.GetItem(recipe.Result?.Id ?? "")?.Icon)));
                var selected = PickCatalogueRows(fishing ? "Add Fish" : "Add Recipe",
                    options.Where(row => !known.Contains(row.Id)).OrderBy(row => row.Name).ToArray());
                if (selected.Length == 0) return;
                if (fishing) foreach (string id in selected) _session.AddFishingEntry(id);
                else _session.AddKnownCatalogueIds(path, selected);
                Changed();
            }));
        }

        var remove = CatalogueAction(footer, recipes || fishing ? "Remove Selected" : "Delete Selected", () =>
        {
            var selected = grid.SelectedItems.Cast<RecordRow>().ToArray();
            if (selected.Length == 0 || !ConfirmCatalogue($"Remove {selected.Length} selected entries?")) return;
            if (fishing) _session.RemoveFishingEntries(selected.Select(row => row.Index));
            else if (recipes) _session.RemoveKnownCatalogueIds(path, selected.Select(row => row.Id.TrimStart('^')));
            else _session.RemoveTeleportEndpoints(selected.Select(row => row.Index));
            Changed();
        });
        editActions.Add(remove);
        remove.IsEnabled = false;

        Button? selectionAction = null;
        if (!recipes && !fishing)
        {
            selectionAction = CatalogueAction(footer, "Travel to System", () =>
            {
                if (grid.SelectedItem is not RecordRow row || row.Portal.Length != 12 ||
                    !ConfirmCatalogue($"Travel to the system for {row.Name}?")) return;
                var endpoint = _session.TeleportEndpoints!.GetObject(row.Index);
                int galaxy = endpoint.GetObject("UniverseAddress")!.GetInt("RealityIndex") + 1;
                _session.TravelToCatalogueSystem(SaveSession.ConvertPlayerPortal(row.Portal, galaxy));
                Changed();
            });
        }
        else if (fishing)
        {
            selectionAction = CatalogueAction(footer, "Edit Catch…", () =>
            {
                if (grid.SelectedItem is not RecordRow row) return;
                var amount = new TextBox { Text = row.Detail };
                var size = new TextBox { Text = row.Extra };
                var fields = new StackPanel { Margin = new Thickness(12) };
                fields.Children.Add(new TextBlock { Text = "Count" });
                fields.Children.Add(amount);
                fields.Children.Add(new TextBlock { Text = "Largest Catch" });
                fields.Children.Add(size);
                var window = new Window
                {
                    Owner = _owner, Title = row.Name, Width = 330, SizeToContent = SizeToContent.Height,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = fields
                };
                CatalogueAction(fields, "Apply", () =>
                {
                    if (!int.TryParse(amount.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedCount)
                        || !double.TryParse(size.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedSize))
                        throw new ArgumentException("Enter a whole count and a numerical catch size.");
                    _session.EditFishingEntry(row.Index, parsedCount, parsedSize);
                    window.DialogResult = true;
                });
                if (window.ShowDialog() == true) Changed();
            });
        }
        if (selectionAction != null) selectionAction.IsEnabled = false;
        grid.SelectionChanged += (_, _) =>
        {
            remove.IsEnabled = grid.SelectedItems.Count > 0;
            if (selectionAction != null) selectionAction.IsEnabled = grid.SelectedItems.Count == 1;
            glyphs.Children.Clear();
            if (recipes || fishing || grid.SelectedItem is not RecordRow row) return;
            if (row.Portal.Length != 12) return;
            glyphs.Children.Add(new TextBlock { Text = "Portal Glyphs", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 8, 0) });
            foreach (char digit in row.Portal) glyphs.Children.Add(_owner.PlayerGlyph(digit));
            glyphs.Children.Add(new TextBlock { Text = row.Detail, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) });
        };

        editActions.Add(CatalogueAction(footer, "Export…", () =>
        {
            var data = new JsonObject();
            if (recipes)
            {
                var ids = new JsonArray();
                foreach (string id in _session.KnownCatalogueIds(path)) ids.Add("^" + id);
                data.Set("KnownRecipes", ids);
            }
            else if (fishing) data.Set(path, _session.FishingRecord);
            else data.Set(path, _session.TeleportEndpoints ?? new JsonArray());
            ExportCatalogueRecords(data, path);
        }));
        editActions.Add(CatalogueAction(footer, "Import…", () =>
        {
            var data = ImportCatalogueRecords();
            if (data == null) return;
            if (recipes)
            {
                var ids = data.GetArray("KnownRecipes") ?? data.GetArray(path)
                    ?? throw new InvalidOperationException("Choose a recipe discovery file.");
                _session.AddKnownCatalogueIds(path, Enumerable.Range(0, ids.Length).Select(ids.GetString).ToArray());
            }
            else if (!fishing || ConfirmCatalogue("Replace the current fishing record with the imported record?"))
                _session.ImportCatalogueRecords(path, data.Get(path)!);
            else return;
            Changed();
        }));

        filter.TextChanged += (_, _) => Filter();
        Refresh();
        return new Border { Style = PageStyle("Card"), Child = panel };
    }

    private IEnumerable<RecordRow> FishingRows()
    {
        var record = _session.FishingRecord;
        if (record?.GetArray("ProductList") is not { } ids) yield break;
        var counts = record.GetArray("ProductCountList");
        var sizes = record.GetArray("LargestCatchList");
        for (int i = 0; i < ids.Length; i++)
        {
            if (ids.Get(i) is not string id || id.TrimStart('^').Length == 0) continue;
            var item = _session.Items.GetItem(id.TrimStart('^'));
            yield return new(i, id, item?.Name ?? id,
                counts != null && i < counts.Length ? counts.GetInt(i).ToString(CultureInfo.InvariantCulture) : "0",
                sizes != null && i < sizes.Length ? sizes.GetDoubleText(i) : "0", LoadImage: () => _owner.LoadIcon(item?.Icon));
        }
    }

    private IEnumerable<RecordRow> RecipeRows(bool all)
    {
        var known = _session.KnownCatalogueIds("KnownRefinerRecipes");
        var lookup = _session.Recipes.ToLookup(recipe => recipe.Id.TrimStart('^'), StringComparer.OrdinalIgnoreCase);
        var ids = all ? lookup.Select(group => group.Key) : known;
        string Describe(RecipeElement part) => $"{part.Amount}× {UiStrings.LocalizeText(_session.Items.GetItem(part.Id)?.Name ?? part.Id)}";
        foreach (string id in ids)
        {
            var recipe = lookup[id].FirstOrDefault();
            var result = recipe?.Result;
            yield return new(0, id, UiStrings.LocalizeText(recipe?.RecipeName ?? id),
                recipe == null ? "" : UiStrings.LocalizeText(recipe.Cooking ? "Cooking" : "Refining"),
                result == null ? "" : Describe(result),
                recipe == null ? "" : string.Join(" + ", recipe.Ingredients.Select(Describe)),
                LoadImage: () => result == null ? null : _owner.LoadIcon(_session.Items.GetItem(result.Id)?.Icon));
        }
    }

    private IEnumerable<RecordRow> TeleportRows()
    {
        var endpoints = _session.TeleportEndpoints;
        if (endpoints == null) yield break;
        for (int i = 0; i < endpoints.Length; i++)
        {
            if (endpoints.Get(i) is not JsonObject endpoint) continue;
            var address = endpoint.GetObject("UniverseAddress");
            var galactic = address?.GetObject("GalacticAddress");
            int galaxy = address?.GetInt("RealityIndex") ?? 0;
            string portal = "", booster = "";
            if (galactic != null)
            {
                int x = galactic.GetInt("VoxelX"), y = galactic.GetInt("VoxelY"), z = galactic.GetInt("VoxelZ");
                int system = galactic.GetInt("SolarSystemIndex");
                portal = CoordinateHelper.VoxelToPortalCode(x, y, z, system, galactic.GetInt("PlanetIndex"));
                booster = CoordinateHelper.VoxelToSignalBooster(x, y, z, system);
            }
            var type = endpoint.GetString("TeleporterType") ?? "";
            yield return new(i, type == "Spacestation" ? "Space Station" : System.Text.RegularExpressions.Regex.Replace(type, "(?<=[a-z])(?=[A-Z])", " "), endpoint.GetString("Name") ?? "",
                $"{GalaxyDatabase.GetGalaxyDisplayName(galaxy)} · {GalaxyDatabase.GetGalaxyType(galaxy)}", "",
                portal, CoordinateHelper.PortalHexToDec(portal), booster);
        }
    }

    private FrameworkElement BuildKnownGlyphs()
    {
        const string key = "KnownGlyphs";
        var panel = new DockPanel();
        var actions = new WrapPanel();
        DockPanel.SetDock(actions, Dock.Bottom);
        panel.Children.Add(actions);
        var status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6) };
        actions.Children.Add(status);
        var glyphs = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4 };
        panel.Children.Add(glyphs);
        var checks = new CheckBox[16];
        bool refreshing = false;
        void Update(int bits)
        {
            refreshing = true;
            for (int i = 0; i < checks.Length; i++) checks[i].IsChecked = (bits & 1 << i) != 0;
            status.Text = UiStrings.Format("discovery.known_count", System.Numerics.BitOperations.PopCount((uint)bits).ToString("N0"), checks.Length.ToString("N0"));
            refreshing = false;
        }
        void Change(int bits)
        {
            _session.SetGlyphBits(bits);
            Update(bits);
            _owner.PageStatus("Glyphs updated in memory. Press Save to keep the changes.");
        }
        for (int i = 0; i < checks.Length; i++)
        {
            int bit = 1 << i;
            var cell = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            cell.Children.Add(_owner.PlayerGlyph("0123456789ABCDEF"[i]));
            var check = new CheckBox { Content = UiStrings.Format("discovery.glyph", i + 1), Margin = new Thickness(3) };
            check.Click += (_, _) =>
            {
                if (!refreshing) Change(check.IsChecked == true ? _session.KnownGlyphBits | bit : _session.KnownGlyphBits & ~bit);
            };
            checks[i] = check;
            cell.Children.Add(check);
            glyphs.Children.Add(cell);
        }
        CatalogueAction(actions, "Learn All", () => Change(0xffff));
        CatalogueAction(actions, "Unlearn All", () => Change(0));
        CatalogueAction(actions, "Export…", () =>
        {
            var data = new JsonObject();
            var entries = new JsonArray();
            foreach (var check in checks) entries.Add(check.IsChecked == true);
            data.Set(key, entries);
            ExportCatalogueRecords(data, key);
        });
        CatalogueAction(actions, "Import…", () =>
        {
            var data = ImportCatalogueRecords();
            if (data == null) return;
            var entries = data.GetArray(key) ?? throw new InvalidOperationException("Choose a glyph discovery file.");
            if (entries.Length != 16) throw new InvalidOperationException("A glyph discovery file must contain 16 check states.");
            int bits = 0;
            for (int i = 0; i < 16; i++) if (entries.GetBool(i)) bits |= 1 << i;
            Change(bits);
        });
        Update(_session.KnownGlyphBits);
        return new Border { Style = PageStyle("Card"), Child = panel };
    }

    private bool ConfirmCatalogue(string message) =>
        MessageBox.Show(_owner, UiStrings.LocalizeText(message), UiStrings.LocalizeText("Catalogue"), MessageBoxButton.OKCancel) == MessageBoxResult.OK;

    private Button CatalogueAction(Panel panel, string label, Action action)
    {
        var button = new Button { Content = label };
        button.Click += (_, _) =>
        {
            try { action(); }
            catch (Exception ex) { _owner.PageError(ex); }
        };
        panel.Children.Add(button);
        return button;
    }

    private void ExportCatalogueRecords(JsonObject data, string name)
    {
        var dialog = new SaveFileDialog { Filter = DiscoveryFilter, DefaultExt = ".nmsdiscover", FileName = name };
        if (dialog.ShowDialog(_owner) == true) data.ExportToFile(dialog.FileName);
    }

    private JsonObject? ImportCatalogueRecords()
    {
        var dialog = new OpenFileDialog { Filter = DiscoveryFilter };
        return dialog.ShowDialog(_owner) == true ? JsonObject.ImportFromFile(dialog.FileName) : null;
    }
}
