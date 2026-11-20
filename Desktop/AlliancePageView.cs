using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using NMSE.Data;
using NMSE.Next.Engine;

namespace NMSE.Next.Desktop;

public sealed class AlliancePageView : Grid
{
    internal DataGrid LeaderboardGrid { get; }

    public AlliancePageView(AlliancePageData data)
    {
        Margin = new Thickness(4);
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var heading = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 2, 4, 8) };
        heading.Children.Add(ConceptIcons.Make("Alliance", 26));
        heading.Children.Add(new TextBlock
        {
            Text = UiStrings.Get("ui.alliance.title"),
            Style = (Style)Application.Current.FindResource("Heading"),
            Margin = new Thickness(9, 0, 0, 0)
        });
        Children.Add(heading);

        var status = new TextBlock
        {
            Text = data.Status,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 4, 2, 12),
            FontSize = 13
        };
        status.SetResourceReference(TextBlock.ForegroundProperty, "Secondary");
        var statusCard = new Border
        {
            Style = (Style)Application.Current.FindResource("Card"),
            Padding = new Thickness(12),
            Child = status
        };
        SetRow(statusCard, 1);
        Children.Add(statusCard);

        var profile = new Grid { Margin = new Thickness(0, 10, 0, 10) };
        profile.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        profile.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        profile.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        profile.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        profile.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddProfileValue(profile, 0, "Alliance name", "Unavailable");
        AddProfileValue(profile, 1, "Member count", "Unavailable");
        AddProfileValue(profile, 2, "Alliance score", "Unavailable");
        var edit = new Button
        {
            Content = "Edit alliance details",
            IsEnabled = false,
            ToolTip = "Alliance profile editing is not available in this build."
        };
        Grid.SetRow(edit, 1);
        Grid.SetColumn(edit, 0);
        Grid.SetColumnSpan(edit, 3);
        edit.Margin = new Thickness(4, 8, 4, 0);
        profile.Children.Add(edit);
        var profileCard = new Border
        {
            Style = (Style)Application.Current.FindResource("Card"),
            Padding = new Thickness(10),
            Child = profile
        };
        SetRow(profileCard, 2);
        Children.Add(profileCard);

        var localData = new StackPanel();
        localData.Children.Add(new TextBlock
        {
            Text = "Saved alliance data",
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(4, 2, 4, 7)
        });
        var stationList = new StackPanel();
        if (data.Stations.Count == 0)
            AddEmptyMessage(stationList, "No saved destinations with an Alliance ID were found.");
        else
            foreach (var station in data.Stations)
                AddLocalRecord(stationList, station.Name, $"{station.Type} · AllianceId data: {station.AllianceIdData}");
        localData.Children.Add(stationList);
        if (data.ReportedAlliances.Count > 0)
        {
            localData.Children.Add(new TextBlock
            {
                Text = "Reported alliances",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(4, 10, 4, 6)
            });
            foreach (var alliance in data.ReportedAlliances)
            {
                var label = alliance.Name.Length > 0 ? alliance.Name : alliance.Id;
                if (alliance.Tag.Length > 0) label += $"  [{alliance.Tag}]";
                AddLocalRecord(localData, label, alliance.Id.Length > 0 ? $"ID {alliance.Id}" : "Account record");
            }
        }
        var localCard = new Border
        {
            Style = (Style)Application.Current.FindResource("Card"),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 8),
            Child = new ScrollViewer
            {
                Content = localData,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 210
            }
        };
        SetRow(localCard, 3);
        Children.Add(localCard);

        var board = new Grid();
        board.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        board.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        board.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var boardHeading = new DockPanel { Margin = new Thickness(4, 7, 4, 7) };
        var viewButton = new Button { Content = UiStrings.Get("ui.alliance.leaderboard"), IsEnabled = false, ToolTip = data.Status };
        DockPanel.SetDock(viewButton, Dock.Right);
        boardHeading.Children.Add(viewButton);
        boardHeading.Children.Add(new TextBlock
        {
            Text = "Alliance rankings",
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        board.Children.Add(boardHeading);

        var stats = new StackPanel { Margin = new Thickness(4, 2, 4, 10) };
        stats.Children.Add(new TextBlock { Text = "Alliance stats", FontWeight = FontWeights.SemiBold, Margin = new Thickness(4) });
        var metrics = new WrapPanel();
        foreach (string label in new[] { "Systems discovered", "Stations claimed", "Plants discovered", "Minerals discovered", "Creatures discovered" })
        {
            var item = new StackPanel { MinWidth = 140, Margin = new Thickness(8, 4, 12, 4) };
            item.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
            var unavailable = new TextBlock { Text = "Unavailable", Margin = new Thickness(0, 3, 0, 0) };
            unavailable.SetResourceReference(TextBlock.ForegroundProperty, "Secondary");
            item.Children.Add(unavailable);
            metrics.Children.Add(item);
        }
        stats.Children.Add(metrics);
        SetRow(stats, 1);
        board.Children.Add(stats);

        LeaderboardGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            ItemsSource = data.Rankings,
            MinHeight = 120,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow
        };
        LeaderboardGrid.SetResourceReference(Control.BackgroundProperty, "Surface");
        LeaderboardGrid.SetResourceReference(Control.ForegroundProperty, "Text");
        LeaderboardGrid.SetResourceReference(Control.BorderBrushProperty, "Edge");
        LeaderboardGrid.Columns.Add(new DataGridTextColumn { Header = "Rank", Binding = new Binding(nameof(AllianceLeaderboardRow.Rank)), Width = new DataGridLength(100) });
        LeaderboardGrid.Columns.Add(new DataGridTextColumn { Header = "Alliance name", Binding = new Binding(nameof(AllianceLeaderboardRow.AllianceName)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        LeaderboardGrid.Columns.Add(new DataGridTextColumn { Header = UiStrings.Get("ui.alliance.score"), Binding = new Binding(nameof(AllianceLeaderboardRow.Score)), Width = new DataGridLength(160) });
        SetRow(LeaderboardGrid, 2);
        board.Children.Add(LeaderboardGrid);

        if (data.Rankings.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = "No ranking values are shown until an official data source is available.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(8),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "Secondary");
            LeaderboardGrid.Visibility = Visibility.Collapsed;
            board.Children.Add(empty);
            SetRow(empty, 2);
        }

        var boardCard = new Border
        {
            Style = (Style)Application.Current.FindResource("Card"),
            Padding = new Thickness(10),
            Child = board
        };
        SetRow(boardCard, 4);
        Children.Add(boardCard);
    }

    static void AddLocalRecord(Panel panel, string title, string detail)
    {
        var row = new Grid { Margin = new Thickness(4, 2, 4, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new TextBlock { Text = title, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        var details = new TextBlock { Text = detail, Margin = new Thickness(12, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
        details.SetResourceReference(TextBlock.ForegroundProperty, "Secondary");
        Grid.SetColumn(details, 1);
        row.Children.Add(details);
        panel.Children.Add(row);
    }

    static void AddEmptyMessage(Panel panel, string text)
    {
        var message = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 2, 4, 2) };
        message.SetResourceReference(TextBlock.ForegroundProperty, "Secondary");
        panel.Children.Add(message);
    }

    static void AddProfileValue(Grid grid, int column, string label, string value)
    {
        var stack = new StackPanel { Margin = new Thickness(6) };
        var heading = new TextBlock { Text = label, FontWeight = FontWeights.SemiBold };
        var content = new TextBlock { Text = value, Margin = new Thickness(0, 5, 0, 0) };
        content.SetResourceReference(TextBlock.ForegroundProperty, "Secondary");
        stack.Children.Add(heading);
        stack.Children.Add(content);
        Grid.SetColumn(stack, column);
        grid.Children.Add(stack);
    }
}
