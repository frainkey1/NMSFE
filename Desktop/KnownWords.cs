using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using NMSE.Data;
using NMSE.Next.Engine;

namespace NMSE.Next.Desktop;

internal sealed partial class DomainPageView
{
    private sealed class WordFlag(string group, int race, bool known) : INotifyPropertyChanged
    {
        public string Group { get; } = group;
        public int Race { get; } = race;
        public bool Known { get; private set; } = known;
        public event PropertyChangedEventHandler? PropertyChanged;

        public void Update(bool value)
        {
            if (Known == value) return;
            Known = value;
            PropertyChanged?.Invoke(this, new(nameof(Known)));
        }
    }

    private sealed record WordRow(WordEntry Word, WordFlag?[] Flags)
    {
        public string Text => Word.Text;
        public string Id => Word.Id;
    }

    private FrameworkElement BuildKnownWords()
    {
        var panel = new DockPanel();
        var search = new TextBox { ToolTip = "Filter by word or ID", Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(search, Dock.Top);
        panel.Children.Add(search);
        var actions = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        DockPanel.SetDock(actions, Dock.Bottom);
        panel.Children.Add(actions);
        var count = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6) };
        actions.Children.Add(count);

        var words = _session.CatalogueWords;
        var races = SaveSession.WordRaces;
        var known = _session.ReadKnownWordFlags();
        var flags = new Dictionary<(string, int), WordFlag>();
        var rows = words.Select(word => new WordRow(word, races.Select(race =>
        {
            if (word.GetGroupForRace(race.Index) is not { } group) return null;
            var key = (group, race.Index);
            if (!flags.TryGetValue(key, out var flag))
                flags[key] = flag = new WordFlag(group, race.Index, known.Contains(key));
            return flag;
        }).ToArray())).ToArray();

        var grid = MakeCatalogueGrid(includeItemColumns: false);
        panel.Children.Add(grid);

        void UpdateCount()
        {
            var (have, total) = _session.WordCompletion();
            count.Text = $"Known: {have:N0} / {total:N0}";
        }

        void Change(Action update)
        {
            try
            {
                update();
                var current = _session.ReadKnownWordFlags();
                foreach (var (key, flag) in flags) flag.Update(current.Contains(key));
                UpdateCount();
                _owner.PageStatus("Word changes applied in memory. Press Save to keep them.");
            }
            catch (Exception ex) { _owner.PageError(ex); }
        }

        foreach (var (label, property) in new[] { ("Word", nameof(WordRow.Text)), ("Word ID", nameof(WordRow.Id)) })
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = label, Binding = new Binding(property),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 95
            });

        for (int i = 0; i < races.Count; i++)
        {
            var (name, race) = races[i];
            var header = new StackPanel();
            header.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
            foreach (bool learn in new[] { true, false })
            {
                var button = new Button { Content = UiStrings.Get(learn ? "discovery.learn_all" : "discovery.unlearn_all"), Padding = new Thickness(4, 2, 4, 2) };
                button.Click += (_, _) => Change(() => _session.SetCatalogueWords(words, learn, race));
                header.Children.Add(button);
            }

            var checkbox = new FrameworkElementFactory(typeof(CheckBox));
            checkbox.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            checkbox.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            checkbox.SetBinding(TagProperty, new Binding($"Flags[{i}]"));
            checkbox.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
                new Binding($"Flags[{i}].Known") { Mode = BindingMode.OneWay, FallbackValue = false });
            var style = new Style(typeof(CheckBox), PageStyleForCheckBox());
            var unavailable = new DataTrigger { Binding = new Binding($"Flags[{i}]"), Value = null };
            unavailable.Setters.Add(new Setter(IsEnabledProperty, false));
            style.Triggers.Add(unavailable);
            checkbox.SetValue(StyleProperty, style);
            checkbox.AddHandler(Button.ClickEvent, new RoutedEventHandler((sender, _) =>
            {
                if (sender is CheckBox { Tag: WordFlag flag } box)
                    Change(() => _session.SetCatalogueWord(flag.Group, flag.Race, box.IsChecked == true));
            }));
            grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = header, CellTemplate = new DataTemplate { VisualTree = checkbox },
                CanUserSort = false, MinWidth = 93, Width = new DataGridLength(1, DataGridLengthUnitType.Star)
            });
        }

        Button ActionButton(string label, Action action)
        {
            var button = new Button { Content = label };
            button.Click += (_, _) =>
            {
                try { action(); }
                catch (Exception ex) { _owner.PageError(ex); }
            };
            actions.Children.Add(button);
            return button;
        }

        ActionButton("Add all missing", () => Change(_session.AddMissingCatalogueWords));
        ActionButton(UiStrings.Get("discovery.unlearn_all"), () =>
        {
            if (MessageBox.Show(_owner, UiStrings.LocalizeText("Unlearn all catalogue words for every race?"), UiStrings.LocalizeText("Unlearn all"), MessageBoxButton.OKCancel) == MessageBoxResult.OK)
                Change(() => _session.SetCatalogueWords(words, false));
        });
        var selectedActions = new List<Button>();
        foreach (bool learn in new[] { true, false })
            selectedActions.Add(ActionButton(learn ? "Learn selected" : "Unlearn selected", () =>
                Change(() => _session.SetCatalogueWords(grid.SelectedItems.Cast<WordRow>().Select(row => row.Word).ToArray(), learn))));
        foreach (var button in selectedActions) button.IsEnabled = false;
        grid.SelectionChanged += (_, _) =>
        {
            foreach (var button in selectedActions) button.IsEnabled = grid.SelectedItems.Count > 0;
        };

        ActionButton("Export…", () =>
        {
            var dialog = new SaveFileDialog { Filter = UiStrings.LocalizeText(DiscoveryFilter), DefaultExt = ".nmsdiscover", FileName = "KnownWords" };
            if (dialog.ShowDialog(_owner) != true) return;
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(new { KnownWords = _session.ExportCatalogueWords() }));
            _owner.PageStatus("Known words exported.");
        });
        ActionButton("Import…", () =>
        {
            var dialog = new OpenFileDialog { Filter = UiStrings.LocalizeText(DiscoveryFilter) };
            if (dialog.ShowDialog(_owner) != true) return;
            using var data = JsonDocument.Parse(File.ReadAllText(dialog.FileName));
            if (data.RootElement.ValueKind != JsonValueKind.Object
                || !data.RootElement.TryGetProperty("KnownWords", out var list) || list.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Choose a Known Words discovery file.");
            var entries = list.EnumerateArray().Select(value => value.GetString() ?? "").ToArray();
            Change(() =>
            {
                var (applied, skipped) = _session.ImportCatalogueWords(entries);
                count.ToolTip = $"Last import: {applied:N0} matched, {skipped:N0} skipped.";
            });
        });

        grid.ItemsSource = rows;
        search.TextChanged += (_, _) =>
        {
            var query = search.Text.Trim();
            grid.ItemsSource = rows.Where(row => row.Text.Contains(query, StringComparison.OrdinalIgnoreCase)
                || row.Id.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        };
        UpdateCount();
        return new Border { Style = PageStyle("Card"), Child = panel };
    }

    private Style PageStyleForCheckBox() => (Style)Application.Current.FindResource(typeof(CheckBox));
}