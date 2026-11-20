namespace NMSE.Next.Engine;

/// <summary>Sidebar order and identifiers for the primary editor pages.</summary>
public static class MainPageCatalog
{
    public const int Player = 0;
    public const int Exosuit = 1;
    public const int MultiTools = 2;
    public const int Starships = 3;
    public const int Fleet = 4;
    public const int Exocraft = 5;
    public const int Companions = 6;
    public const int Bases = 7;
    public const int SpaceStation = 8;
    // Keep the old identifier for saved navigation state and existing callers.
    public const int Construction = SpaceStation;
    public const int Catalogue = 9;
    public const int Milestones = 10;
    public const int Settlements = 11;
    public const int ByteBeats = 12;
    public const int AccountRewards = 13;
    public const int ExportSettings = 14;
    public const int RawJsonEditor = 15;
    public const int DatabaseSearch = 16;
    public const int Corvettes = 17;

    public static readonly string[] Names =
    [
        "Player", "Exosuit", "Multi-tools", "Starships", "Fleet", "Exocraft",
        "Companions", "Bases & Storage", "Space Station", "Catalogue", "Milestones",
        "Settlements", "ByteBeats", "Account Rewards", "Export Settings",
        "Raw JSON Editor", "Database Search", "Corvettes"
    ];

    // Corvettes have a dedicated page while retaining the existing shared ship slots.
    public static readonly int[] NavigationOrder =
    [
        Player, Exosuit, MultiTools, Starships, Fleet, Corvettes, Exocraft, Companions,
        Bases, SpaceStation, Catalogue, Milestones, Settlements, ByteBeats, AccountRewards,
        ExportSettings, RawJsonEditor, DatabaseSearch
    ];
}
