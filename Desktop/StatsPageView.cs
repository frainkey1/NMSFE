using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using NMSE.Data;
using NMSE.Next.Engine;

namespace NMSE.Next.Desktop;

internal sealed class StatsPageView : Grid
{
 sealed record NavTarget(string Tab,string? SectionKey,string Label);
 readonly SaveSession _session;
 readonly Action<string> _status;
 readonly Action<string[]> _navigateJson;
 readonly List<Button> _items=[];
 readonly Grid _contentRoot=new();
 readonly StackPanel _groups=new();
 readonly ScrollViewer _statsScroll=new();
 readonly Button _applyButton=new(){Content="✓  Apply changes",MinWidth=150};
 readonly Dictionary<string,(TextBox Box,int Baseline,bool Editable)> _fields=new(StringComparer.Ordinal);
 string _activeTab="Milestones";
 string? _activeSection;

 static readonly string[] MainSections=["Milestones","Lifeforms","Factions"];

 public bool HasPending=>_fields.Values.Any(f=>f.Box.Text!=f.Baseline.ToString(CultureInfo.InvariantCulture));

 public StatsPageView(SaveSession session,Action<string> status,Action<string[]> navigateJson)
 {
  _session=session;_status=status;_navigateJson=navigateJson;
  Margin=new Thickness(8);
  RowDefinitions.Add(new(){Height=GridLength.Auto});
  RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});

  var heading=new StackPanel{Margin=new Thickness(4,2,4,10)};
  heading.Children.Add(new TextBlock{Text="Stats",Style=PageStyle("Heading")});
  heading.Children.Add(new TextBlock{Text="Milestones, lifeforms, factions, and other save statistics.",Margin=new Thickness(0,2,0,0)});
  var jsonButton=new Button{Content=UiStrings.Get("ui.common.json_statistics"),ToolTip=UiStrings.Get("ui.common.json_statistics_tooltip"),Padding=new Thickness(8,4,8,4),Margin=new Thickness(2,7,2,2)};
  jsonButton.Click+=(_,_)=>_navigateJson(["PlayerStateData","Stats"]);heading.Children.Add(jsonButton);
  Children.Add(heading);

  var body=new Grid();
  body.ColumnDefinitions.Add(new(){Width=new GridLength(235)});
  body.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
  Grid.SetRow(body,1);Children.Add(body);

  var nav=new StackPanel();
  foreach(string tab in MainSections)AddNavItem(nav,new(tab,null,tab),0);
  var divider=new Border{Height=1,Margin=new Thickness(8,9,8,7)};
  divider.SetResourceReference(Border.BackgroundProperty,"Edge");nav.Children.Add(divider);
  var otherHeader=new TextBlock{Text=UiStrings.Get("milestone.tab_other").ToUpperInvariant(),FontSize=10,FontWeight=FontWeights.SemiBold,Margin=new Thickness(10,1,6,4)};
  otherHeader.SetResourceReference(TextBlock.ForegroundProperty,"Secondary");nav.Children.Add(otherHeader);
  foreach(var section in _session.GetStatsSections("Other Stats"))AddNavItem(nav,new("Other Stats",section.Key,section.Name),1);

  var navScroll=new ScrollViewer{Content=nav,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
  var navCard=new Border{Style=PageStyle("Card"),Padding=new Thickness(7),Child=navScroll,Margin=new Thickness(0,0,10,0)};
  body.Children.Add(navCard);

  _contentRoot.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
  _contentRoot.RowDefinitions.Add(new(){Height=GridLength.Auto});
  _statsScroll.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;
  _statsScroll.HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;
  _statsScroll.Content=_groups;
  _contentRoot.Children.Add(_statsScroll);
  var footer=new WrapPanel{Margin=new Thickness(2,10,2,1),VerticalAlignment=VerticalAlignment.Center};
  _applyButton.Style=PageStyle("Primary");_applyButton.Click+=(_,_)=>{if(Commit())RenderContent();};
  footer.Children.Add(_applyButton);
  var hint=new TextBlock{Text="Edits apply in memory. Press Save to write them to the save file.",Margin=new Thickness(10,0,0,0),VerticalAlignment=VerticalAlignment.Center};
  hint.SetResourceReference(TextBlock.ForegroundProperty,"Secondary");footer.Children.Add(hint);
  Grid.SetRow(footer,1);_contentRoot.Children.Add(footer);
  var contentCard=new Border{Style=PageStyle("Card"),Padding=new Thickness(14),Child=_contentRoot};
  Grid.SetColumn(contentCard,1);body.Children.Add(contentCard);
  ShowTab("Milestones",null);
 }

 void AddNavItem(Panel parent,NavTarget target,int indent)
 {
  var button=new Button{Content=target.Label,Tag=target,HorizontalContentAlignment=HorizontalAlignment.Left,Padding=new Thickness(10,7,8,7),Margin=new Thickness(indent==0?1:10,1,1,1),MinHeight=32};
  button.Click+=(_,_)=>{if(!Commit())return;ShowTab(target.Tab,target.SectionKey);};
  _items.Add(button);parent.Children.Add(button);
 }

 void ShowTab(string tab,string? section)
 {
  _activeTab=tab;_activeSection=section;
  foreach(var button in _items)
  {
   var target=(NavTarget)button.Tag;
   bool selected=target.Tab==tab&&(tab!="Other Stats"||target.SectionKey==section);
   button.SetResourceReference(Control.BackgroundProperty,selected?"AccentSurface":"Surface");
   button.SetResourceReference(Control.ForegroundProperty,selected?"OnAccent":"Text");
   button.SetResourceReference(Control.BorderBrushProperty,selected?"Accent":"Edge");
   button.BorderThickness=new Thickness(selected?1:0.5);
  }
  RenderContent();
 }

 void RenderContent()
 {
  _groups.Children.Clear();_fields.Clear();
  if(_session.Player==null)
  {
   _groups.Children.Add(EmptyState("Load a save to view its stats."));
   _applyButton.IsEnabled=false;return;
  }
  _applyButton.IsEnabled=true;
  IEnumerable<StatsSection> sections=_activeTab=="Other Stats"
   ?_session.GetStatsSections(_activeTab).Where(s=>s.Key==_activeSection)
   :_session.GetStatsSections(_activeTab);
  var cards=new WrapPanel();
  foreach(var section in sections)
  {
   var values=_session.GetStats(_activeTab,section.Key);
   if(_activeTab=="Other Stats"&&values.Count==0)continue;
   var card=MakeCard(section.Name);
   var rows=(StackPanel)card.Child!;
   if(_activeTab=="Other Stats"&&values.Any(v=>v.SubcategoryKey!=null))
   {
    foreach(var category in values.GroupBy(v=>v.SubcategoryKey))
    {
     if(category.Key!=null)rows.Children.Add(Subheading(category.First().SubcategoryName??category.Key));
     foreach(var value in category)rows.Children.Add(MakeValueRow(value));
    }
   }
   else foreach(var value in values)rows.Children.Add(MakeValueRow(value));
   cards.Children.Add(card);
  }
  if(cards.Children.Count==0)
  {
   _groups.Children.Add(EmptyState(_activeTab=="Other Stats"?"No stat fields are available in this save section.":"No stat groups are available in this save."));
   return;
  }
  _groups.Children.Add(cards);
 }

 internal bool Commit()
 {
  if(_fields.Count==0)return true;
  var updates=new Dictionary<string,int>(StringComparer.Ordinal);
  foreach(var (id,field) in _fields)
  {
   if(!int.TryParse(field.Box.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out int value))
   {
    _status($"Enter a whole number for {id}.");
    field.Box.Focus();field.Box.SelectAll();return false;
   }
   if(field.Editable&&value!=field.Baseline)updates[id]=value;
  }
  if(_session.ApplyStats(updates))_status("Stats changes applied in memory. Press Save to keep them.");
  return true;
 }

 UIElement MakeValueRow(StatsValue value)
 {
  var row=new Grid{Margin=new Thickness(0,1,0,1)};
  row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
  row.ColumnDefinitions.Add(new(){Width=new GridLength(120)});
  row.ColumnDefinitions.Add(new(){Width=new GridLength(value.ShowGuildRank?140:0)});
  var label=new TextBlock{Text=value.Name,VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(2,4,6,4),ToolTip=value.StatId};
  row.Children.Add(label);
  var number=new TextBox{Text=value.Value.ToString(CultureInfo.InvariantCulture),MinWidth=92,Padding=new Thickness(5,3,5,3),VerticalContentAlignment=VerticalAlignment.Center,IsEnabled=value.Exists};
  number.SetResourceReference(Control.BackgroundProperty,"Input");number.SetResourceReference(Control.ForegroundProperty,"Text");number.SetResourceReference(Control.BorderBrushProperty,"Edge");number.ToolTip=value.Exists?value.StatId:"This stat has no editable value in the loaded save.";
  Grid.SetColumn(number,1);row.Children.Add(number);_fields[value.StatId]=(number,value.Value,value.Exists);
  if(value.ShowGuildRank)
  {
   string rank=value.NextRankIn<0?UiStrings.Get("milestone.guild_rank_max"):$"{UiStrings.Get("milestone.guild_promo_in")} {value.NextRankIn.ToString("N0",CultureInfo.CurrentCulture)}";
   var rankLabel=new TextBlock{Text=$"{value.GuildRank} / {value.MaxGuildRank}  ·  {rank}",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(7,0,0,0),FontSize=11};
   rankLabel.SetResourceReference(TextBlock.ForegroundProperty,"Secondary");Grid.SetColumn(rankLabel,2);row.Children.Add(rankLabel);
  }
  return row;
 }

 static Border MakeCard(string title)
 {
  var stack=new StackPanel();
  stack.Children.Add(new TextBlock{Text=title,FontSize=16,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,7)});
  return new Border{Style=PageStyle("Card"),Padding=new Thickness(10),Margin=new Thickness(1,0,10,10),MinWidth=290,MaxWidth=430,Child=stack};
 }

 static UIElement Subheading(string title)
 {
  var label=new TextBlock{Text=title,FontWeight=FontWeights.SemiBold,Margin=new Thickness(1,7,1,3)};
  label.SetResourceReference(TextBlock.ForegroundProperty,"Secondary");return label;
 }

 static Border EmptyState(string text)
 {
  var label=new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,FontSize=14,Margin=new Thickness(4,8,4,8)};
  label.SetResourceReference(TextBlock.ForegroundProperty,"Secondary");
  return new Border{Padding=new Thickness(14),CornerRadius=new CornerRadius(8),Background=(System.Windows.Media.Brush)Application.Current.FindResource("Surface"),Child=label};
 }

 static Style PageStyle(string name)=>(Style)Application.Current.FindResource(name);
}
