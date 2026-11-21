using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using NMSE.Data;
using NMSE.Core.Utilities;
using NMSE.Models;
using NMSE.Next.Engine;

namespace NMSE.Next.Desktop;

internal sealed partial class DomainPageView
{
    private void ShowCompanionStats(JsonObject pet)
    {
        _form.Children.Add(new TextBlock
        {
            Text = UiStrings.Get("editor.companion_stats"),
            Style = PageStyle("Heading"),
            FontSize = 16,
            Margin = new Thickness(4, 3, 4, 8)
        });

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var identity = StatsCard(columns, 0, UiStrings.Get("companion.identity_behaviour"));
        AddIfPresent(identity, pet, "CreatureID", "Species");
        AddIfPresent(identity, pet, "CustomName", "Name");
        AddNestedIfPresent(identity, pet.GetObject("CreatureType"), "CreatureType", "Type");
        AddNestedIfPresent(identity, pet.GetObject("Biome"), "Biome", "Biome");
        AddIfPresent(identity, pet, "Predator", "Predator");
        AddIfPresent(identity, pet, "HasFur", "Has fur");
        AddIfPresent(identity, pet, "Scale", "Scale");
        AddIfPresent(identity, pet, "Trust", "Trust");
        AddIfPresent(identity, pet, "BirthTime", "Birth time");
        AddIfPresent(identity, pet, "LastEggTime", "Last egg time");
        AddIfPresent(identity, pet, "CustomSpeciesName", "Custom species name");
        AddIfPresent(identity, pet, "EggModified", "Egg modified");
        AddIfPresent(identity, pet, "HasBeenSummoned", "Summoned");
        AddIfPresent(identity, pet, "AllowUnmodifiedReroll", "Allow reroll");
        AddIfPresent(identity, pet, "UA", "Universal address");
        AddArrayValues(identity, pet.GetArray("Traits"), ["Helpfulness", "Aggression", "Independence"]);
        AddArrayValues(identity, pet.GetArray("Moods"), ["Hungry", "Lonely"]);

        var genes = StatsCard(columns, 1, UiStrings.Get("companion.seeds_trust_history"));
        AddSeed(genes, pet, "CreatureSeed", "Creature seed");
        AddSeed(genes, pet, "CreatureSecondarySeed", "Secondary seed");
        AddIfPresent(genes, pet, "SpeciesSeed", "Species seed");
        AddIfPresent(genes, pet, "GenusSeed", "Genus seed");
        AddSeed(genes, pet, "BoneScaleSeed", "Bone scale seed");
        AddSeed(genes, pet, "ColourBaseSeed", "Colour base seed");
        AddIfPresent(genes, pet, "LastTrustIncreaseTime", "Trust increase");
        AddIfPresent(genes, pet, "LastTrustDecreaseTime", "Trust decrease");

        _form.Children.Add(columns);
    }

    private StackPanel StatsCard(Grid host, int column, string title)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = title,
            Style = PageStyle("Heading"),
            FontSize = 15,
            Margin = new Thickness(3, 1, 3, 7)
        });
        var border = new Border { Style = PageStyle("Card"), Padding = new Thickness(9), Margin = new Thickness(3), Child = panel };
        Grid.SetColumn(border, column);
        host.Children.Add(border);
        return panel;
    }

    private static string LocalizedStatsLabel(string label)
    {
        string? key = label switch
        {
            "Species" => "companion.species",
            "Name" => "companion.name",
            "Type" => "companion.type",
            "Biome" => "companion.biome",
            "Predator" => "companion.predator",
            "Has fur" => "companion.visible_stats_has_fur",
            "Scale" => "companion.scale",
            "Trust" => "companion.trust",
            "Birth time" => "companion.visible_stats_birth_time",
            "Last egg time" => "companion.visible_stats_last_egg_time",
            "Custom species name" => "companion.visible_stats_custom_species_name",
            "Egg modified" => "companion.visible_stats_egg_modified",
            "Summoned" => "companion.summoned",
            "Allow reroll" => "companion.visible_stats_allow_reroll",
            "Universal address" => "companion.visible_stats_universal_address",
            "Helpfulness" => "companion.helpfulness",
            "Aggression" => "companion.aggression",
            "Independence" => "companion.independence",
            "Hungry" => "companion.hungry",
            "Lonely" => "companion.lonely",
            "Creature seed" => "companion.visible_stats_creature_seed",
            "Secondary seed" => "companion.visible_stats_secondary_seed",
            "Species seed" => "companion.visible_stats_species_seed",
            "Genus seed" => "companion.visible_stats_genus_seed",
            "Bone scale seed" => "companion.visible_stats_bone_scale_seed",
            "Colour base seed" => "companion.visible_stats_colour_base_seed",
            "Trust increase" => "companion.visible_stats_trust_increase",
            "Trust decrease" => "companion.visible_stats_trust_decrease",
            _ => null
        };

        if (key is null) return UiStrings.LocalizeText(label);
        return UiStrings.Get(key).Trim().TrimEnd(':', '：');
    }

    private void AddIfPresent(Panel panel, JsonObject source, string key, string label)
    {
        if (source.Get(key) is { } value)
            AddField(panel, source, key, LocalizedStatsLabel(label), value);
    }

    private void AddNestedIfPresent(Panel panel, JsonObject? source, string key, string label)
    {
        if (source?.Get(key) is { } value)
            AddField(panel, source, key, LocalizedStatsLabel(label), value);
    }

    private void AddArrayValues(Panel panel, JsonArray? values, string[] labels)
    {
        if (values is null) return;
        for (int i = 0; i < Math.Min(values.Length, labels.Length); i++)
            if (values.Get(i) is { } value)
                AddField(panel, values, i, LocalizedStatsLabel(labels[i]), value);
    }

    private void AddSeed(Panel panel, JsonObject pet, string key, string label)
    {
        if (pet.GetArray(key) is not { Length: > 1 } seed) return;

        string localizedLabel = LocalizedStatsLabel(label);
        AddField(panel, seed, 0, UiStrings.Format("companion.visible_stats_seed_enabled", localizedLabel), seed.Get(0));
        string baseline = SaveSession.FieldText(seed.Get(1));
        var box = new TextBox { Text = baseline, MinWidth = 130, ToolTip = localizedLabel };
        var generate = new Button { Content = UiStrings.Get("common.generate"), Margin = new Thickness(5, 0, 0, 0) };
        var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(165) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new TextBlock { Text = localizedLabel, Style = PageStyle("Label"), TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(box, 1);
        row.Children.Add(box);
        Grid.SetColumn(generate, 2);
        row.Children.Add(generate);
        panel.Children.Add(row);

        string? Normalize(string text) => SeedHelper.NormalizeSeed(text);
        _fields.Add((new FieldEdit(seed, 1, baseline, baseline), () => Normalize(box.Text) ?? box.Text.Trim()));
        _draftValidators.Add(() => box.Text == baseline || Normalize(box.Text) is not null);
        generate.Click += (_, _) => box.Text = "0x" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
    }
}
