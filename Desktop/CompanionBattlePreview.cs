using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NMSE.Core;

namespace NMSE.Next.Desktop;

public partial class MainWindow
{
    private async Task VerifyBattlePreview()
    {
        string source = _session.SourcePath ?? throw new InvalidOperationException("Load a save for battle verification.");
        string output = Path.Combine(Path.GetDirectoryName(_session.ResourceRoot)!, "Artifacts", "Preview12");
        Directory.CreateDirectory(output);
        try
        {
            SelectNavigation(6);
            RenderSelectedPage();
            var page = _domainPages[6];
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            static IEnumerable<T> Controls<T>(DependencyObject root) where T : DependencyObject
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                {
                    var child = VisualTreeHelper.GetChild(root, i);
                    if (child is T match) yield return match;
                    foreach (var nested in Controls<T>(child)) yield return nested;
                }
            }

            Controls<Button>(page).Single(button => Equals(button.Content, "Battle team"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            var pet = _session.Player!.GetArray("Pets")!.GetObject(0)!;
            var team = _session.Player.GetObject("PetBattleTeam")!.GetArray("TeamMembers")!;
            ComboBox Picker(string name) => Controls<ComboBox>(page)
                .Single(picker => System.Windows.Automation.AutomationProperties.GetName(picker) == name);
            var roster = Picker("Slot 2");
            roster.SelectedIndex = 1;
            if (team.GetObject(1)!.GetInt("PetIndex") != 0 ||
                Enumerable.Range(0, team.Length).Count(i => team.GetObject(i)?.GetInt("PetIndex") == 0) != 1)
                throw new Exception("Roster assignment or duplicate removal failed.");
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            var ability = Picker("Ability Slot 1");
            int next = ability.SelectedIndex == 1 ? 2 : 1;
            string previous = pet.GetArray("PetBattlerMoves")!.GetString(0)!;
            ability.SelectedIndex = next;
            string equipped = pet.GetArray("PetBattlerMoves")!.GetString(0)!;
            if (equipped == previous || equipped == "^") throw new Exception("Ability selection did not reach the save data.");
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);

            var search = Controls<Button>(page).Single(button => Equals(button.Content, "Search Seed…"));
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            int ticks = 0;
            timer.Tick += (_, _) =>
            {
                var dialog = OwnedWindows.OfType<CompanionBattleSeedWindow>().SingleOrDefault();
                if (dialog == null) return;
                if (++ticks == 1)
                    Controls<Button>(dialog).Single(button => Equals(button.Content, "Search")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var apply = Controls<Button>(dialog).Single(button => Equals(button.Content, "Apply"));
                if (apply.IsEnabled)
                    apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                else if (ticks >= 90)
                    dialog.Close();
            };
            string oldSeed = pet.GetString("SpeciesSeed")!;
            timer.Start();
            try { search.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
            finally { timer.Stop(); }
            if (pet.GetString("SpeciesSeed") == oldSeed) throw new Exception("Seed search did not apply a result.");
            _session.ResetCompanionBattleRecord(pet);
            if (pet.GetInt("PetBattlerVictories") != 0 || pet.GetArray("PetBattlerMoves")!.GetString(0) != equipped)
                throw new Exception("Reset did not preserve the equipped ability.");
            File.WriteAllText(Path.Combine(output, "battle-result.txt"), "PASS: roster assignment, duplicate removal, ability selection, seed search and apply, record reset preserves abilities.");
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(output, "battle-result.txt"), ex.ToString());
        }
        finally
        {
            _session.Load(source);
            ClearDomainPageState();
            Close();
        }
    }
}
