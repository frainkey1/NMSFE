using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Controls.Primitives;
using NMSE.Core;
using NMSE.Data;

namespace NMSE.Next.Desktop;

internal sealed class CompanionBattleSeedWindow : Window
{
    private static readonly long[] Budgets = [2_000_000, 10_000_000, 50_000_000];
    private readonly ComboBox[] _classes = new ComboBox[3];
    private readonly TextBox[] _targets = new TextBox[3];
    private readonly ComboBox _priority = new() { ItemsSource = new[] { UiStrings.Get("companion.battle_seed_priority_balanced"), UiStrings.Get("companion.battle_seed_col_health"), UiStrings.Get("companion.battle_seed_col_speed"), UiStrings.Get("companion.battle_seed_col_combat") }, SelectedIndex = 0 };
    private readonly CheckBox _adjust = new() { Content = "Adjust scale (changes visible size)" };
    private readonly Button _search = new() { Content = "Search" };
    private readonly Button _deeper = new() { Content = "Search Deeper", IsEnabled = false };
    private readonly Button _apply = new() { Content = "Apply", IsEnabled = false };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _details = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ListView _results = new();
    private CancellationTokenSource? _running;
    private PetBattleLogic.SolveRequest? _lastRequest;
    private int _budget;

    internal PetBattleLogic.SolveCandidate? Result { get; private set; }
    internal bool ApplyScale { get; private set; }

    internal CompanionBattleSeedWindow(double scale, int health, int speed, int combat)
    {
        Title = "Pet Battle Seed Search";
        Width = 720;
        Height = 610;
        MinWidth = 480;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "Surface");
        SetResourceReference(ForegroundProperty, "Text");
        var root = new DockPanel { Margin = new Thickness(14) };
        Content = root;
        var options = new StackPanel();
        DockPanel.SetDock(options, Dock.Top);
        root.Children.Add(options);
        options.Children.Add(new TextBlock
        {
            Text = "Find species and genus seeds for the desired battle classes. target = numerical value. Leave a target blank to maximize that stat.",
            TextWrapping = TextWrapping.Wrap
        });
        var fields = new UniformGrid { Columns = 3 };
        options.Children.Add(fields);
        string[] names = ["Health", "Agility", "Combat"];
        for (int i = 0; i < names.Length; i++)
        {
            var column = new StackPanel { Margin = new Thickness(4) };
            fields.Children.Add(column);
            column.Children.Add(new TextBlock { Text = UiStrings.Get($"companion.battle_seed_{names[i].ToLowerInvariant()}").TrimEnd(':') + " " + UiStrings.Get("companion.battle_seed_class_suffix") });
            _classes[i] = new ComboBox { ItemsSource = new[] { "C", "B", "A", "S" }, SelectedIndex = 3 };
            column.Children.Add(_classes[i]);
            column.Children.Add(new TextBlock { Text = UiStrings.Get($"companion.battle_seed_{names[i].ToLowerInvariant()}").TrimEnd(':') + " " + UiStrings.Get("companion.battle_seed_target_suffix") });
            _targets[i] = new TextBox();
            column.Children.Add(_targets[i]);
        }
        var actions = new WrapPanel();
        options.Children.Add(actions);
        actions.Children.Add(new TextBlock { Text = UiStrings.Get("ui.companion.priority"), VerticalAlignment = VerticalAlignment.Center });
        actions.Children.Add(_priority);
        actions.Children.Add(_adjust);
        actions.Children.Add(_search);
        actions.Children.Add(_deeper);
        options.Children.Add(_status);

        var footer = new StackPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        footer.Children.Add(_details);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        footer.Children.Add(buttons);
        buttons.Children.Add(_apply);
        var close = new Button { Content = "Close", IsCancel = true };
        buttons.Children.Add(close);

        var view = new GridView();
        foreach (var (header, path, width) in new (string, string, int)[]
        {
            (UiStrings.Get("companion.battle_seed_col_health"), "Candidate.Stats.Health", 100), (UiStrings.Get("companion.battle_seed_col_speed"), "Candidate.Stats.Speed", 100),
            (UiStrings.Get("companion.battle_seed_col_combat"), "Candidate.Stats.Combat", 100), (UiStrings.Get("companion.battle_seed_col_scale"), "Scale", 120), (UiStrings.Get("companion.battle_seed_col_match"), "Match", 100)
        })
            view.Columns.Add(new GridViewColumn { Header = header, Width = width, DisplayMemberBinding = new Binding(path) });
        _results.View = view;
        _results.SetResourceReference(BackgroundProperty, "Input");
        _results.SetResourceReference(ForegroundProperty, "Text");
        root.Children.Add(_results);

        _results.SelectionChanged += (_, _) =>
        {
            _apply.IsEnabled = _running == null && _results.SelectedItem is SearchRow;
            _details.Text = _results.SelectedItem is SearchRow row
                ? string.Format(CultureInfo.CurrentCulture, UiStrings.Get("companion.battle_seed_details_seed_format"), row.Candidate.SpeciesSeed, row.Candidate.GenusSeed,
                    PetBattleLogic.GetGameBarFraction(PetBattleLogic.StatKind.Health, row.Candidate.Stats.Health),
                    PetBattleLogic.GetGameBarFraction(PetBattleLogic.StatKind.Speed, row.Candidate.Stats.Speed),
                    PetBattleLogic.GetGameBarFraction(PetBattleLogic.StatKind.Combat, row.Candidate.Stats.Combat))
                : "";
        };
        _search.Click += async (_, _) =>
        {
            if (_running != null) { _running.Cancel(); return; }
            try
            {
                _lastRequest = new PetBattleLogic.SolveRequest(
                    new(_classes[2].SelectedIndex, _classes[1].SelectedIndex, _classes[0].SelectedIndex),
                    Target(0), Target(1), Target(2), scale, _adjust.IsChecked == true, health, speed, combat,
                    HealthWeight: _priority.SelectedIndex == 1 ? 4 : 1,
                    SpeedWeight: _priority.SelectedIndex == 2 ? 4 : 1,
                    CombatWeight: _priority.SelectedIndex == 3 ? 4 : 1);
                _budget = 0;
                await RunSearch();
            }
            catch (ArgumentException ex) { _status.Text = ex.Message; }
        };
        _deeper.Click += async (_, _) => { _budget++; await RunSearch(); };
        _apply.Click += (_, _) =>
        {
            if (_running != null || _results.SelectedItem is not SearchRow row) return;
            Result = row.Candidate;
            ApplyScale = _lastRequest?.AdjustScale == true;
            DialogResult = true;
        };
        foreach (var picker in _classes.Append(_priority))
            picker.SelectionChanged += (_, _) => InvalidateResults();
        foreach (var target in _targets) target.TextChanged += (_, _) => InvalidateResults();
        _adjust.Checked += (_, _) => InvalidateResults();
        _adjust.Unchecked += (_, _) => InvalidateResults();
        Closed += (_, _) => _running?.Cancel();
    }

    private sealed record SearchRow(PetBattleLogic.SolveCandidate Candidate, string Scale, string Match);

    private void InvalidateResults()
    {
        _deeper.IsEnabled = false;
        _results.ItemsSource = null;
        _apply.IsEnabled = false;
        _status.Text = "";
    }

    private float? Target(int index)
    {
        string value = _targets[index].Text.Trim();
        if (value.Length == 0) return null;
        if (!int.TryParse(value, out int result) || result < 1)
            throw new ArgumentException("Enter a positive whole number for each target, or leave it blank.");
        return result;
    }

    private async Task RunSearch()
    {
        if (_lastRequest is not { } original || _running != null) return;
        var request = original with { MaxCandidates = Budgets[_budget] };
        using var cancellation = new CancellationTokenSource();
        _running = cancellation;
        _search.Content = "Cancel";
        _deeper.IsEnabled = false;
        _apply.IsEnabled = false;
        _results.ItemsSource = null;
        foreach (Control input in _classes.Cast<Control>().Concat(_targets).Append(_priority).Append(_adjust))
            input.IsEnabled = false;
        _status.Text = UiStrings.Get("ui.companion.searching");
        var progress = new Progress<long>(count =>
        {
            if (ReferenceEquals(_running, cancellation))
                _status.Text = string.Format(CultureInfo.CurrentCulture, UiStrings.Get("ui.companion.search_progress"), count);
        });
        try
        {
            var candidates = await Task.Run(() => PetBattleLogic.Solve(request, cancellation.Token, progress));
            cancellation.Token.ThrowIfCancellationRequested();
            var reference = PetBattleLogic.GetReference(request);
            _results.ItemsSource = candidates.Select(candidate => new SearchRow(candidate,
                candidate.Scale.ToString("G7", CultureInfo.CurrentCulture),
                Math.Clamp(reference.IdealScore > 0 ? candidate.Score / reference.IdealScore : candidate.Score, 0, 1).ToString("P1"))).ToArray();
            _results.SelectedIndex = candidates.Count > 0 ? 0 : -1;
            _status.Text = candidates.Count == 0 ? UiStrings.Get("ui.dynamic.87fdeee0c152") : string.Format(CultureInfo.CurrentCulture, UiStrings.Get("ui.companion.search_matches"), candidates.Count);
            _deeper.IsEnabled = _budget < Budgets.Length - 1;
        }
        catch (OperationCanceledException) { _status.Text = UiStrings.LocalizeText("Search cancelled."); }
        catch (Exception ex) { _status.Text = ex.Message; }
        finally
        {
            _running = null;
            _search.Content = "Search";
            _apply.IsEnabled = _results.SelectedItem is SearchRow;
            foreach (Control input in _classes.Cast<Control>().Concat(_targets).Append(_priority).Append(_adjust))
                input.IsEnabled = true;
        }
    }
}
