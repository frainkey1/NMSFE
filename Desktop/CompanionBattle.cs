using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using NMSE.Core;
using NMSE.Core.Utilities;
using NMSE.Data;
using NMSE.Models;
using NMSE.Next.Engine;

namespace NMSE.Next.Desktop;

internal sealed partial class DomainPageView
{
    private static readonly string[] BattleClasses = ["C", "B", "A", "S"];
    private static readonly string[] BattleStats = ["Health", "Agility", "Combat"];
    private sealed record BattleChoice(string Value, string Label);

    private void ShowBattle(JsonObject pet)
    {
        bool movesLoaded = _session.LoadBattleCatalog();
        AddBattleRoster();

        var stats = BattleCard("Stat class overrides");
        const string overrideKey = "PetBattlerUseCoreStatClassOverrides";
        if (pet.Get(overrideKey) is bool)
            AddField(stats, pet, overrideKey, "Override pet classes", pet.Get(overrideKey));
        var overrides = pet.GetArray("PetBattlerCoreStatClassOverrides");
        bool active = pet.Get(overrideKey) is true;
        for (int i = 0; active && i < BattleStats.Length; i++)
            if (overrides != null && i < overrides.Length && overrides.Get(i) is JsonObject stat && stat.Get("InventoryClass") is string)
                AddField(stats, stat, "InventoryClass", BattleStats[i], stat.Get("InventoryClass"));

        var levels = pet.GetArray("PetBattlerTreatsEaten");
        int Level(int index) => levels != null && index < levels.Length && levels.Get(index) != null ? levels.GetInt(index) : 0;
        double scale = pet.Get("Scale") == null ? 0 : pet.GetDouble("Scale");
        if (ParseBattleSeed(pet.GetString("SpeciesSeed")) is ulong species &&
            ParseBattleSeed(pet.GetString("GenusSeed")) is ulong genus)
        {
            var rolled = PetBattleLogic.RollClasses(species, genus);
            int ClassAt(int index) => Math.Max(0, Array.IndexOf(BattleClasses,
                overrides != null && index < overrides.Length ? overrides.GetObject(index)?.GetString("InventoryClass") : null));
            var effective = active ? new PetBattleLogic.Classes(ClassAt(2), ClassAt(1), ClassAt(0)) : rolled;
            char Letter(int value) => PetBattleLogic.ClassLetter(value);
            BattleText(stats, $"True classes   Health {Letter(rolled.Health)} · Agility {Letter(rolled.Speed)} · Combat {Letter(rolled.Combat)} · Overall {Letter(rolled.Overall)}");
            BattleText(stats, active ? $"Average class   {Letter(effective.Overall)}" : "Average class   Procedural");
            var predicted = PetBattleLogic.PredictCoreStats(species, genus, (float)scale, effective, Level(0), Level(1), Level(2));
            BattleText(stats, $"Predicted stats   Health {predicted.Health:N0} · Speed {predicted.Speed:N0} · Combat {predicted.Combat:N0}");
        }

        var search = new Button { Content = UiStrings.Get("ui.companion.search_seed"), HorizontalAlignment = HorizontalAlignment.Left };
        search.Click += (_, _) =>
        {
            if (!Commit()) return;
            var dialog = new CompanionBattleSeedWindow(pet.GetDouble("Scale"), Level(0), Level(1), Level(2)) { Owner = _owner };
            if (dialog.ShowDialog() != true || dialog.Result is not { } result) return;
            var edits = new List<FieldEdit>
            {
                new(pet, "SpeciesSeed", SeedHelper.FormatSeed(result.SpeciesSeed), SaveSession.FieldText(pet.Get("SpeciesSeed"))),
                new(pet, "GenusSeed", SeedHelper.FormatSeed(result.GenusSeed), SaveSession.FieldText(pet.Get("GenusSeed")))
            };
            if (dialog.ApplyScale)
                edits.Add(new(pet, "Scale", result.Scale.ToString(CultureInfo.InvariantCulture), SaveSession.FieldText(pet.Get("Scale"))));
            ChangeBattle(() => _session.ApplyFields(edits));
        };
        search.IsEnabled = pet.Get("SpeciesSeed") is string && pet.Get("GenusSeed") is string && pet.Get("Scale") != null;
        stats.Children.Add(search);

        var record = BattleCard("Battle record and genetic profile");
        AddBattleField(record, pet, "PetBattlerVictories", "Holo-Arena victories");
        AddBattleField(record, pet, "PetBattleProgressToTreat", "Mutation progress");
        AddBattleField(record, pet, "PetBattlerTreatsAvailable", "Gene edits available");
        if (levels != null)
        {
            BattleText(record, $"Genes improved   {Level(0) + Level(1) + Level(2)} / 30");
            string treatsEatenLabel = UiStrings.LocalizeText("treats eaten");
            for (int i = 0; i < Math.Min(BattleStats.Length, levels.Length); i++)
            {
                string statLabel = UiStrings.LocalizeText(BattleStats[i]);
                AddField(record, levels, i, $"{statLabel} {treatsEatenLabel}", levels.Get(i));
            }
        }
        var reset = new Button { Content = "Reset Battle Record", HorizontalAlignment = HorizontalAlignment.Left };
        reset.Click += (_, _) =>
        {
            if (MessageBox.Show(_owner, UiStrings.LocalizeText("Reset this companion’s victories, mutation progress, gene edits and class overrides? Equipped abilities are kept."),
                UiStrings.LocalizeText("Reset Battle Record"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes)
                ChangeBattle(() => _session.ResetCompanionBattleRecord(pet));
        };
        record.Children.Add(reset);

        AddCompanionAffinity(pet);
        AddLegacyTreatsCard(levels);
        AddBattleAbilities(pet, movesLoaded);
    }

    private void AddLegacyTreatsCard(JsonArray? levels)
    {
        if (levels is null) return;
        var card = BattleCard("Pet Battler Treats Eaten");
        BattleText(card, "Treats fed toward each battle stat.");
        string treatsEatenLabel = UiStrings.LocalizeText("treats eaten");
        for (int i = 0; i < Math.Min(BattleStats.Length, levels.Length); i++)
        {
            string statLabel = UiStrings.LocalizeText(BattleStats[i]);
            BattleText(card, $"{statLabel} {treatsEatenLabel}   {SaveSession.FieldText(levels.Get(i))}");
        }
    }

    private void AddCompanionAffinity(JsonObject pet)
    {
        var affinity = BattleCard("Affinity");
        string key = PetBiomeAffinityMap.ResolveAffinity(pet.GetString("CreatureID") ?? "", pet.GetObject("Biome")?.GetString("Biome") ?? "");
        BattleText(affinity, string.IsNullOrEmpty(key) ? "Unknown" : PetBiomeAffinityMap.GetAffinityDisplayName(key));
        if (PetBiomeAffinityMap.GetAffinityMatchup(PetBiomeAffinityMap.GetAffinityGameName(key)) is { } matchup)
        {
            BattleText(affinity, $"Weak against   {PetBiomeAffinityMap.FormatAffinityList(matchup.Weak)}");
            BattleText(affinity, $"Strong against   {PetBiomeAffinityMap.FormatAffinityList(matchup.Strong)}");
        }
    }

    private void AddBattleRoster()
    {
        var card = BattleCard("Pet Battle Team");
        var members = _session.Player?.GetObject("PetBattleTeam")?.GetArray("TeamMembers");
        if (members == null)
        {
            BattleText(card, "This save has no battle team.");
            return;
        }
        var choices = new List<BattleChoice>();
        foreach (var entry in _entries)
            choices.Add(new(entry.Index.ToString(CultureInfo.InvariantCulture), $"Pet {entry.Index} · {entry.Title}"));

        for (int i = 0; i < members.Length; i++)
        {
            if (members.Get(i) is not JsonObject member || member.Get("PetIndex") == null) continue;
            string current = SaveSession.FieldText(member.Get("PetIndex"));
            var available = new List<BattleChoice> { new("-1", $"Empty {i + 1}") };
            available.AddRange(choices);
            if (!available.Any(choice => choice.Value == current))
                available.Add(new(current, $"Pet {current} · unavailable"));
            var picker = BattlePicker(card, $"Slot {i + 1}", available, current);
            picker.SelectionChanged += (_, _) =>
            {
                if (picker.SelectedItem is not BattleChoice choice || choice.Value == current) return;
                ChangeBattle(() =>
                {
                    var changes = new List<FieldEdit> { new(member, "PetIndex", choice.Value, current) };
                    if (choice.Value != "-1")
                        for (int slot = 0; slot < members.Length; slot++)
                            if (members.Get(slot) is JsonObject other && other != member &&
                                SaveSession.FieldText(other.Get("PetIndex")) == choice.Value)
                                changes.Add(new(other, "PetIndex", "-1", choice.Value));
                    _session.ApplyFields(changes);
                });
            };
        }
    }

    private void AddBattleAbilities(JsonObject pet, bool loaded)
    {
        var card = BattleCard("Abilities");
        if (!loaded || pet.GetArray("PetBattlerMoves") is not { } moves)
        {
            BattleText(card, loaded ? "This companion has no saved ability slots." : "The ability catalog could not be loaded.");
            return;
        }
        var moveList = pet.GetArray("PetBattlerMoveList");
        for (int i = 0; i < moves.Length; i++)
        {
            int slot = i;
            string current = moves.GetString(slot) ?? "^";
            string id = current.TrimStart('^');
            var choices = new List<BattleChoice> { new("^", "None") };
            choices.AddRange(PetBattleMovesetDatabase.Movesets.SelectMany(set => set.Slots)
                .Where(rule => rule.SlotNumber == slot + 1).SelectMany(rule => rule.Options)
                .Select(option => option.Template).Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(move => !CompanionLogic.RestrictedMoveIds.Contains(move, StringComparer.OrdinalIgnoreCase))
                .Select(move => PetBattleMoveDatabase.ById.GetValueOrDefault(move))
                .OfType<PetBattleMoveEntry>()
                .Select(move => new BattleChoice("^" + move.Id, $"{move.Id} · {move.LocalisedDescription}")));
            if (!choices.Any(choice => choice.Value == current)) choices.Add(new(current, id));
            var picker = BattlePicker(card, $"Ability Slot {slot + 1}", choices, current);
            BattleText(card, string.IsNullOrEmpty(id) ? "Ability ID   None" : $"Ability ID   {id}");
            if (!string.IsNullOrEmpty(id))
            {
                var cooldownOptions = PetBattleMovesetDatabase.Movesets
                    .SelectMany(set => set.Slots.Where(rule => rule.SlotNumber == slot + 1)
                        .SelectMany(rule => rule.Options))
                    .Where(option => string.Equals(option.Template, id, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (cooldownOptions.Length > 0)
                {
                    int minimum = cooldownOptions.Min(option => option.CooldownMin);
                    int maximum = cooldownOptions.Max(option => option.CooldownMax);
                    string cooldownRange = minimum == maximum ? $"{minimum} turn(s)" : $"{minimum}–{maximum} turns";
                    BattleText(card, $"Ability cooldown range   {cooldownRange}");
                }
            }
            if (moveList != null && slot < moveList.Length && moveList.GetObject(slot) is { } moveDetails)
            {
                string moveTemplate = (moveDetails.GetString("MoveTemplateID") ?? "^").TrimStart('^');
                if (!string.IsNullOrEmpty(moveTemplate) && !string.Equals(moveTemplate, id, StringComparison.OrdinalIgnoreCase))
                    BattleText(card, $"Move list ID   {moveTemplate}");

            }
            picker.SelectionChanged += (_, _) =>
            {
                if (picker.SelectedItem is BattleChoice choice && choice.Value != current)
                    ChangeBattle(() => _session.ApplyFields([new(moves, slot, choice.Value, current)]));
            };
            if (!PetBattleMoveDatabase.ById.TryGetValue(id, out var equipped)) continue;
            BattleText(card, $"{equipped.IconStyleDisplay} · {equipped.TargetDisplay} · Multi-turn {(equipped.MultiTurnMove ? "Yes" : "No")} · Basic move {(equipped.BasicMove ? "Yes" : "No")}");
            var sets = PetBattleMovesetDatabase.FindMovesetsContainingMove(id);
            if (sets.Count > 0) BattleText(card, string.Join(" · ", sets.Select(set => Readable(set.Id))));
            for (int phase = 0; phase < equipped.Phases.Count; phase++)
                BattleText(card, $"Phase {phase + 1}   {equipped.Phases[phase].EffectDisplay} · {equipped.Phases[phase].StrengthDisplay}");
        }
    }

    private void ChangeBattle(Action action)
    {
        if (!Commit()) return;
        try
        {
            action();
            _owner.PageStatus("Battle changes applied in memory. Press Save to keep them.");
        }
        catch (Exception ex) { _owner.PageError(ex); }
        RefreshBattle();
    }

    private void RefreshBattle()
    {
        if (_entry == null) return;
        double offset = _details.VerticalOffset;
        Select(_entry);
        _details.ScrollToVerticalOffset(offset);
    }

    private StackPanel BattleCard(string title)
    {
        var card = new StackPanel();
        card.Children.Add(new TextBlock { Text = title, Style = PageStyle("Heading"), FontSize = 17 });
        _form.Children.Add(new Border { Child = card, Style = PageStyle("Card") });
        return card;
    }

    private static void BattleText(Panel card, string text) =>
        card.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) });

    private static ComboBox BattlePicker(Panel card, string label, List<BattleChoice> choices, string current)
    {
        BattleText(card, label);
        var picker = new ComboBox
        {
            ItemsSource = choices, DisplayMemberPath = nameof(BattleChoice.Label),
            SelectedItem = choices.First(choice => choice.Value == current)
        };
        System.Windows.Automation.AutomationProperties.SetName(picker, label);
        card.Children.Add(picker);
        return picker;
    }

    private void AddBattleField(Panel card, JsonObject pet, string key, string label)
    {
        if (pet.Get(key) is { } value) AddField(card, pet, key, label, value);
    }

    private static ulong? ParseBattleSeed(string? seed)
    {
        if (seed == null) return null;
        if (seed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return ulong.TryParse(seed.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong hex) ? hex : null;
        return ulong.TryParse(seed, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong number) ? number : null;
    }
}
