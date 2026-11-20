using System.Windows;
using NMSE.Data;
using NMSE.Models;
using NMSE.Next.Engine;

namespace NMSE.Next.Desktop;

public partial class MainWindow
{
    readonly Dictionary<int, DomainPageView> _domainPages = [];
    StatsPageView? _statsPage;
    SettlementsPageView? _settlementsPage;
    AccountRewardsPageView? _accountRewardsPage;
    long _viewRevision;

    internal string PageName(int page) => _pages[page];
    internal void PageStatus(string text) => Status.Text = UiStrings.LocalizeText(text);
    internal void PageError(Exception ex) => Error(ex);
    internal (List<SlotView>, int, int) MakeSlotModels(JsonObject inventory) => BuildSlotModels(inventory);

    bool DomainPagesHavePendingChanges =>
        _domainPages.Values.Any(p => p.HasPending) ||
        _settlementsPage?.HasPending == true ||
        _accountRewardsPage?.HasPending == true;

    bool CommitDomainPages() =>
        _domainPages.Values.All(p => p.Commit()) &&
        (_settlementsPage?.Commit() ?? true) &&
        (_accountRewardsPage?.Commit() ?? true);

    void ClearDomainPageState()
    {
        _domainPages.Clear();
        _statsPage = null;
        _settlementsPage = null;
        _accountRewardsPage = null;
    }

    void ShowDomainPage(int page)
    {
        if (page == MainPageCatalog.AccountRewards)
        {
            _accountRewardsPage ??= new AccountRewardsPageView(this, _session);
            MigrationPage.Child = _accountRewardsPage;
            MigrationPage.Visibility = Visibility.Visible;
            Workspace.Visibility = Visibility.Collapsed;
            return;
        }
        if (page == MainPageCatalog.Settlements)
        {
            _settlementsPage ??= new SettlementsPageView(_session, PageStatus, PageError, NavigateToRawJsonPath);
            MigrationPage.Child = _settlementsPage;
            MigrationPage.Visibility = Visibility.Visible;
            Workspace.Visibility = Visibility.Collapsed;
            return;
        }
        if (page == MainPageCatalog.Milestones)
        {
            _statsPage ??= new StatsPageView(_session, PageStatus, NavigateToRawJsonPath);
            MigrationPage.Child = _statsPage;
            MigrationPage.Visibility = Visibility.Visible;
            Workspace.Visibility = Visibility.Collapsed;
            return;
        }
        if (!_domainPages.TryGetValue(page, out var view))
            _domainPages[page] = view = new(this, _session, page);
        view.Repaint();
        MigrationPage.Child = view;
        MigrationPage.Visibility = Visibility.Visible;
        Workspace.Visibility = Visibility.Collapsed;
    }
}
