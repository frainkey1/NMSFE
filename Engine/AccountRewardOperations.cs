using NMSE.Core;
using NMSE.Data;
using NMSE.Models;

namespace NMSE.Next.Engine;

public sealed record AccountRewardRow(string Id, string Name, string Category, string ProductId,
    string? Icon, int Season, int Stage, bool MustBeUnlocked, bool Unlocked, bool Redeemed);
public sealed record AccountRewardEdit(string Id, bool Unlocked, bool Redeemed);
public sealed record AccountRewardConsistencyIssue(string Id, string Name, string CurrentArray,
    string MissingArray, string Description);

public sealed partial class SaveSession
{
    public IReadOnlyList<AccountRewardRow> ReadAccountRewardRows(string section)
    {
        var rewards = RewardDatabase.Rewards.Where(r => section switch
        {
            "season" or "redeemed-season" => r.Category == "season",
            "twitch" or "redeemed-twitch" => r.Category == "twitch",
            "platform" or "redeemed-platform" => r.Category == "platform",
            "special" => r.Category == "entitlement",
            _ => false
        }).ToList();

        var settings = AccountSettings;
        var player = Player;
        string accountArray = section switch
        {
            "season" => "UnlockedSeasonRewards",
            "twitch" => "UnlockedTwitchRewards",
            "platform" => "UnlockedPlatformRewards",
            _ => ""
        };
        string redeemArray = section switch
        {
            "season" or "redeemed-season" => "RedeemedSeasonRewards",
            "twitch" or "redeemed-twitch" => "RedeemedTwitchRewards",
            "platform" or "redeemed-platform" => "RedeemedPlatformRewards",
            _ => ""
        };
        var unlocked = AccountLogic.GetUnlockedSet(string.IsNullOrEmpty(accountArray) ? null : settings?.GetArray(accountArray));
        var redeemed = AccountLogic.GetUnlockedSet(player?.GetArray(redeemArray));
        var knownTech = AccountLogic.GetUnlockedSet(player?.GetArray("KnownTech"));
        var knownSpecials = AccountLogic.GetUnlockedSet(player?.GetArray("KnownSpecials"));
        var result = new List<AccountRewardRow>(rewards.Count);

        foreach (var reward in rewards)
        {
            string productId = string.IsNullOrWhiteSpace(reward.ProductId)
                ? CatalogueLogic.StripCaretPrefix(reward.Id)
                : CatalogueLogic.StripCaretPrefix(reward.ProductId);
            bool entitlement = reward.Category == "entitlement";
            bool isUnlocked = entitlement
                ? ContainsId(settings?.GetArray("UnlockedSpecials"), productId)
                : unlocked.Contains(reward.Id);
            string saveProductId = CatalogueLogic.EnsureCaretPrefix(productId);
            bool isRedeemed = entitlement
                ? knownTech.Contains(saveProductId) || knownSpecials.Contains(saveProductId)
                : redeemed.Contains(reward.Id);
            var item = Items.GetItem(productId) ?? Items.GetItem(reward.Id);
            string name = item?.Name is { Length: > 0 } itemName ? itemName : reward.Name;
            result.Add(new(reward.Id, name, reward.Category, productId, item?.Icon,
                reward.SeasonId, reward.StageId, reward.Unlock, isUnlocked, isRedeemed));
        }

        IEnumerable<string> extras = section switch
        {
            "season" => unlocked.Except(rewards.Select(r => r.Id), StringComparer.OrdinalIgnoreCase),
            "twitch" => AccountLogic.GetUnlockedSet(settings?.GetArray("UnlockedTwitchRewards"))
                .Except(rewards.Select(r => r.Id), StringComparer.OrdinalIgnoreCase),
            "platform" => AccountLogic.GetUnlockedSet(settings?.GetArray("UnlockedPlatformRewards"))
                .Except(rewards.Select(r => r.Id), StringComparer.OrdinalIgnoreCase),
            "special" => AccountLogic.GetUnlockedSet(settings?.GetArray("UnlockedSpecials"))
                .Except(RewardDatabase.EntitlementRewards.Select(r => CatalogueLogic.EnsureCaretPrefix(CatalogueLogic.StripCaretPrefix(r.ProductId))), StringComparer.OrdinalIgnoreCase),
            "redeemed-season" => redeemed.Except(rewards.Select(r => r.Id), StringComparer.OrdinalIgnoreCase),
            "redeemed-twitch" => redeemed.Except(rewards.Select(r => r.Id), StringComparer.OrdinalIgnoreCase),
            "redeemed-platform" => redeemed.Except(rewards.Select(r => r.Id), StringComparer.OrdinalIgnoreCase),
            _ => []
        };
        foreach (string id in extras)
        {
            bool isRedeemed = section.StartsWith("redeemed-", StringComparison.Ordinal) || redeemed.Contains(id);
            bool isUnlocked = section == "special" || unlocked.Contains(id);
            result.Add(new(id, "(unknown reward)", section.StartsWith("redeemed-") ? section[9..] : section,
                CatalogueLogic.StripCaretPrefix(id), null, -1, -1, false, isUnlocked, isRedeemed));
        }
        return result;
    }

    public void ApplyAccountRewardEdits(string section, IReadOnlyList<AccountRewardEdit> edits)
    {
        if (edits.Count == 0) return;
        bool editsAccount = section is "season" or "twitch" or "platform" or "special";
        if (editsAccount && AccountSettings == null)
            throw new InvalidOperationException(AccountLoadMessage ?? "Account data is unavailable.");

        var settings = AccountSettings;
        var player = Player ?? throw new InvalidOperationException("Player save data is unavailable.");
        string accountArray = section switch
        {
            "season" => "UnlockedSeasonRewards",
            "twitch" => "UnlockedTwitchRewards",
            "platform" => "UnlockedPlatformRewards",
            _ => ""
        };
        string redeemedArray = section switch
        {
            "season" or "redeemed-season" => "RedeemedSeasonRewards",
            "twitch" or "redeemed-twitch" => "RedeemedTwitchRewards",
            "platform" or "redeemed-platform" => "RedeemedPlatformRewards",
            _ => ""
        };
        var productIds = RewardDatabase.Rewards.GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key,
            g => CatalogueLogic.StripCaretPrefix(string.IsNullOrWhiteSpace(g.First().ProductId) ? g.First().Id : g.First().ProductId),
            StringComparer.OrdinalIgnoreCase);
        var beforeRedeemed = AccountLogic.GetUnlockedSet(player.GetArray(redeemedArray));
        var redeemedChanges = new List<(string Id, bool Redeemed)>();

        foreach (var edit in edits)
        {
            string productId = productIds.TryGetValue(edit.Id, out var mapped) ? mapped : CatalogueLogic.StripCaretPrefix(edit.Id);
            bool entitlement = RewardDatabase.EntitlementRewards.Any(r => string.Equals(r.Id, edit.Id, StringComparison.OrdinalIgnoreCase));
            if (editsAccount)
            {
                if (entitlement)
                {
                    SetArrayValue(settings!, "UnlockedSpecials", CatalogueLogic.EnsureCaretPrefix(productId), edit.Unlocked);
                    var item = Items.GetItem(productId);
                    if (item != null && !string.IsNullOrEmpty(AccountLogic.GetEntitlementSeenArrayName(item)))
                        SetArrayValue(settings!, "SeenTechnologies", CatalogueLogic.EnsureCaretPrefix(productId), edit.Unlocked);
                }
                else SetArrayValue(settings!, accountArray, edit.Id, edit.Unlocked);
            }

            if (section is "season" or "twitch" or "platform" or "redeemed-season" or "redeemed-twitch" or "redeemed-platform")
            {
                string productSaveId = CatalogueLogic.EnsureCaretPrefix(productId);
                bool old = entitlement
                    ? ArrayContains(player.GetArray("KnownTech"), productSaveId) || ArrayContains(player.GetArray("KnownSpecials"), productSaveId)
                    : beforeRedeemed.Contains(edit.Id);
                if (old != edit.Redeemed)
                {
                    if (entitlement)
                    {
                        AccountLogic.SyncKnownArraysForChangedRewards(Data, [(edit.Id, edit.Redeemed)], Items, productIds);
                    }
                    else
                    {
                        SetArrayValue(player, redeemedArray, edit.Id, edit.Redeemed);
                        if (section is "season" or "twitch" or "redeemed-season" or "redeemed-twitch")
                            redeemedChanges.Add((edit.Id, edit.Redeemed));
                    }
                }
            }
        }

        if (redeemedChanges.Count > 0)
            AccountLogic.SyncKnownArraysForChangedRewards(Data, redeemedChanges, Items, productIds);
        if (editsAccount) AccountDirty = true;
        Dirty = true;
    }

    public IReadOnlyList<AccountRewardConsistencyIssue> CheckAccountRewardConsistency()
    {
        return AccountLogic.CheckConsistencyStructured(Data, Items)
            .Select(i => new AccountRewardConsistencyIssue(i.Id, i.Name, i.CurrentArray, i.MissingArray, i.Description))
            .ToArray();
    }

    public void ResolveAccountRewardConsistency(AccountRewardConsistencyIssue issue, bool addToMissing)
    {
        var player = Player ?? throw new InvalidOperationException("Player save data is unavailable.");
        if (addToMissing) SetArrayValue(player, issue.MissingArray, issue.Id, true);
        else SetArrayValue(player, issue.CurrentArray, issue.Id, false);
        Dirty = true;
    }

    JsonObject? AccountSettings => AccountData?.GetObject("UserSettingsData") ?? AccountData;

    static bool ContainsId(JsonArray? array, string id) =>
        ArrayContains(array, CatalogueLogic.EnsureCaretPrefix(id)) || ArrayContains(array, CatalogueLogic.StripCaretPrefix(id));

    static bool ArrayContains(JsonArray? array, string id)
    {
        if (array == null) return false;
        for (int i = 0; i < array.Length; i++)
            if (string.Equals(array.GetString(i), id, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    static void SetArrayValue(JsonObject owner, string key, string id, bool present)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(id)) return;
        var array = owner.GetArray(key);
        if (present)
        {
            if (array == null) { array = new JsonArray(); owner.Set(key, array); }
            if (!ArrayContains(array, id)) array.Add(id);
        }
        else if (array != null)
        {
            for (int i = array.Length - 1; i >= 0; i--)
                if (string.Equals(array.GetString(i), id, StringComparison.OrdinalIgnoreCase)) array.RemoveAt(i);
        }
    }
}
