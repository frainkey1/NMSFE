using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using NMSE.Data;
using NMSE.Next.Engine;
namespace NMSE.Next.Desktop;

public partial class MainWindow
{
 int _selectedPage=MainPageCatalog.Player;
 bool _moreNavigation;
 readonly List<Button> _navigationButtons=[];
 void BuildNavigationV2()
 {
  Navigation.Children.Clear();_navigationButtons.Clear();
  for(int position=0;position<MainPageCatalog.NavigationOrder.Length;position++)
  {
   int i=MainPageCatalog.NavigationOrder[position];
   if(i==MainPageCatalog.SpaceStation&&!_moreNavigation)
   {
    var more=NavigationButton(UiStrings.Get("navigation.more"),UiStrings.Format("navigation.more_tooltip",MainPageCatalog.NavigationOrder.Length-9),"More");
    more.Click+=(_,_)=>{_moreNavigation=true;BuildNavigationV2();};
    AutomationProperties.SetHelpText(more,UiStrings.Get("navigation.more_help"));
    Navigation.Children.Add(more);
   }
   var button=NavigationButton(NavigationPageLabel(i),_pages[i],_pages[i]);button.Tag=i;
   button.Click+=Navigate;
   button.Visibility=position<9||_moreNavigation?Visibility.Visible:Visibility.Collapsed;
   _navigationButtons.Add(button);Navigation.Children.Add(button);
  }
  if(_moreNavigation)
  {
   var less=NavigationButton(UiStrings.Get("navigation.less"),UiStrings.Get("navigation.less_tooltip"),"Less");
   less.Click+=(_,_)=>{_moreNavigation=false;BuildNavigationV2();};
   AutomationProperties.SetHelpText(less,UiStrings.Get("navigation.less_help"));
   Navigation.Children.Add(less);
  }
  SelectNavigation(_selectedPage);
 }
 string NavigationPageLabel(int page)=>UiStrings.Get(page switch
 {
  MainPageCatalog.Player=>"tab.player",MainPageCatalog.Exosuit=>"tab.exosuit",MainPageCatalog.MultiTools=>"tab.multitools",
  MainPageCatalog.Starships=>"tab.starships",MainPageCatalog.Fleet=>"tab.fleet",MainPageCatalog.Corvettes=>"tab.corvettes",
  MainPageCatalog.Exocraft=>"tab.exocraft",MainPageCatalog.Companions=>"tab.companions",MainPageCatalog.Bases=>"tab.bases_storage",
  MainPageCatalog.SpaceStation=>"tab.space_station",MainPageCatalog.Catalogue=>"tab.discoveries",MainPageCatalog.Milestones=>"tab.milestones",
  MainPageCatalog.Settlements=>"tab.settlements",MainPageCatalog.ByteBeats=>"tab.bytebeats",MainPageCatalog.AccountRewards=>"tab.account_rewards",
  MainPageCatalog.ExportSettings=>"tab.export_settings",MainPageCatalog.RawJsonEditor=>"tab.raw_json_editor",MainPageCatalog.DatabaseSearch=>"tab.database_search",
  _=>"tab.player"
 });
 Button NavigationButton(string name,string tooltip,string? iconName=null)
 {
  var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(38)});row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
  var icon=ConceptIcons.Make(iconName??name,21);icon.HorizontalAlignment=HorizontalAlignment.Left;icon.VerticalAlignment=VerticalAlignment.Center;
  var tile=new Border{CornerRadius=new CornerRadius(7),Width=31,Height=31,HorizontalAlignment=HorizontalAlignment.Left,Child=icon};
  tile.SetResourceReference(Border.BackgroundProperty,"AccentSurface");
  if(icon is System.Windows.Shapes.Path pictogram)
  {
   var brushProperty=Equals(pictogram.Tag,"FilledIcon")?System.Windows.Shapes.Shape.FillProperty:System.Windows.Shapes.Shape.StrokeProperty;
   pictogram.SetResourceReference(brushProperty,"OnAccent");
  }
  icon.HorizontalAlignment=HorizontalAlignment.Center;row.Children.Add(tile);
  var label=new TextBlock{Text=name,FontSize=12,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(label,1);row.Children.Add(label);
  var button=new Button{Content=row,HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(8,8,5,8),Margin=new Thickness(1,2,1,2),ToolTip=tooltip,MinHeight=39};
  button.SetResourceReference(Control.ForegroundProperty,"Text");
  button.Background=Brushes.Transparent;button.BorderBrush=Brushes.Transparent;
  AutomationProperties.SetName(button,name);
  return button;
 }
 void SelectNavigation(int index)
 {
  _selectedPage=index;
  foreach(var button in _navigationButtons)
  {
   bool selected=Equals(button.Tag,index);
   if(selected){button.SetResourceReference(Control.BackgroundProperty,"AccentSurface");button.SetResourceReference(Control.BorderBrushProperty,"Accent");}
   else{button.Background=Brushes.Transparent;button.BorderBrush=Brushes.Transparent;}
   string resource=selected?"OnAccent":"Text";
   button.SetResourceReference(Control.ForegroundProperty,resource);
   if(button.Content is Grid row)
   {
    foreach(var text in row.Children.OfType<TextBlock>()){text.SetResourceReference(TextBlock.ForegroundProperty,resource);text.FontWeight=selected?FontWeights.SemiBold:FontWeights.Normal;}
    foreach(var icon in row.Children.OfType<System.Windows.Shapes.Path>())
    {
     var brushProperty=Equals(icon.Tag,"FilledIcon")?System.Windows.Shapes.Shape.FillProperty:System.Windows.Shapes.Shape.StrokeProperty;
     icon.SetResourceReference(brushProperty,resource);
    }
   }
   AutomationProperties.SetItemStatus(button,selected?"Selected":"");
  }
 }
}
