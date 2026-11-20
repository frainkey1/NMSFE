using System.Windows;
using System.Windows.Controls;
using NMSE.Data;
using NMSE.Next.Engine;
namespace NMSE.Next.Desktop;
public partial class MainWindow
{
 Grid? _playerPageContent;
 SaveSession? _playerPageSession;
 PlayerDraft? _playerPageBaseline;
 readonly List<TextBox> _playerFields=[];
 readonly Dictionary<int,FrameworkElement> _playerTabs=[];
 readonly WrapPanel _playerTabButtons=new();
 Border? _playerTabHost;
 int _playerSelectedTab;
 bool PlayerPageHasPendingChanges=>PlayerAdvancedPending||PlayerKnowledgePending||PlayerGeneralPending||(_playerPageBaseline!=null&&PlayerPageDraft()!=_playerPageBaseline);
 PlayerDraft PlayerPageDraft()=>new(_playerFields[0].Text,_playerFields[1].Text,_playerFields[2].Text,_playerFields[3].Text,_playerFields[4].Text,_playerFields[5].Text,_playerPageBaseline!.SaveName==null?null:_playerFields[6].Text,_playerPageBaseline.SaveSummary==null?null:_playerFields[7].Text);
 void ClearPlayerPageState()
 {
  if(_playerTabButtons.Parent is Panel previousTabs)previousTabs.Children.Remove(_playerTabButtons);
  _playerPageContent=null;_playerPageSession=null;_playerPageBaseline=null;_playerFields.Clear();_playerTabs.Clear();_playerTabButtons.Children.Clear();_playerTabHost=null;
  ClearPlayerGeneralState();ClearPlayerKnowledgeState();ClearPlayerAdvancedState();
 }
 void ShowPlayerPage()
 {
  if(_busy)return;if(!ReferenceEquals(_playerPageSession,_session))ClearPlayerPageState();
  if(_playerPageContent==null)
  {
   _playerPageSession=_session;_playerPageContent=new Grid();
   _playerPageContent.RowDefinitions.Add(new(){Height=GridLength.Auto});_playerPageContent.RowDefinitions.Add(new(){Height=GridLength.Auto});_playerPageContent.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
   var heading=new StackPanel{Orientation=Orientation.Horizontal};heading.Children.Add(ConceptIcons.Make("Player",24));heading.Children.Add(new TextBlock{Text=UiStrings.Get("player.section_stats"),Style=(Style)FindResource("Heading"),Margin=new Thickness(8,2,3,8)});_playerPageContent.Children.Add(heading);
   string[] labels=[UiStrings.Get("player.tab_general"),UiStrings.Get("player.tab_guide"),UiStrings.Get("player.tab_titles"),UiStrings.Get("player.tab_multiplayer"),UiStrings.Get("player.tab_hotkeys")];
   for(int i=0;i<labels.Length;i++){int tab=i;var button=new Button{Content=labels[i],Tag=i,Padding=new Thickness(14,7,14,7)};button.Click+=(_,_)=>{if(CommitPlayerPage())SelectPlayerTab(tab);};_playerTabButtons.Children.Add(button);}
   Grid.SetRow(_playerTabButtons,1);_playerPageContent.Children.Add(_playerTabButtons);_playerTabHost=new Border();Grid.SetRow(_playerTabHost,2);_playerPageContent.Children.Add(_playerTabHost);SelectPlayerTab(_playerSelectedTab);
  }
  MigrationPage.Child=_playerPageContent;MigrationPage.Visibility=Visibility.Visible;Workspace.Visibility=Visibility.Collapsed;
 }
 void SelectPlayerTab(int index)
 {
  _playerSelectedTab=index;
  foreach(Button b in _playerTabButtons.Children){bool selected=(int)b.Tag==index;b.SetResourceReference(Control.BackgroundProperty,selected?"AccentSurface":"Surface");b.SetResourceReference(Control.ForegroundProperty,selected?"OnAccent":"Text");}
  if(!_playerTabs.TryGetValue(index,out var content))
  {
   content=_session.SourcePath==null?new TextBlock{Text="Open a save to view Player information.",Margin=new Thickness(8)}:index switch{0=>BuildPlayerGeneralPage(),1=>BuildPlayerGuidePage(),2=>BuildPlayerTitlesPage(),3=>BuildPlayerMultiplayerPage(),4=>BuildPlayerHotkeysPage(),_=>throw new ArgumentOutOfRangeException()};
   _playerTabs[index]=content;
  }
  _playerTabHost!.Child=content;
 }
 bool CommitPlayerPage()
 {
  if(_busy)return false;
  try
  {
   if(_playerPageBaseline!=null)_session.ApplyPlayer(PlayerPageDraft(),true);
   if(!CommitPlayerGeneral(true)||!CommitPlayerAdvancedPages()||!CommitPlayerKnowledgePages()||!CommitPlayerGeneral())return false;
   if(_playerPageBaseline!=null){var draft=PlayerPageDraft();if(draft!=_playerPageBaseline){_session.ApplyPlayer(draft);_playerPageBaseline=draft;}}
   return true;
  }catch(Exception ex){Error(ex);return false;}
 }
}
