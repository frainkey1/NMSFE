using System.Windows;
using System.Windows.Controls;
using NMSE.Data;
using NMSE.Next.Engine;
namespace NMSE.Next.Desktop;

public partial class MainWindow
{
 bool PlayerKnowledgePending => false;
 bool CommitPlayerKnowledgePages()=>true;
 void ClearPlayerKnowledgeState() { }
 FrameworkElement BuildPlayerGuidePage()=>BuildKnowledgePage(false);
 FrameworkElement BuildPlayerTitlesPage()=>BuildKnowledgePage(true);
 FrameworkElement BuildKnowledgePage(bool titles)
 {
  var session=_session;
  var page=new StackPanel{MaxWidth=1100};
  page.Children.Add(new TextBlock{Text=titles?"Titles":"Guide",Style=(Style)FindResource("Heading")});
  bool available=titles?session.TitleKnowledgeAvailable:session.GuideKnowledgeAvailable;
  page.Children.Add(new TextBlock{Text=titles?UiStrings.LocalizeText("Account: ")+session.PlayerAccountDisplayName:"Guide topics are shared by this account.",Margin=new Thickness(4,4,4,10),TextWrapping=TextWrapping.Wrap});
  if(!available)
  {
   page.Children.Add(new TextBlock{Text=session.AccountLoadMessage??"This account does not contain the required knowledge arrays.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(4,8,4,12)});
  }
  var filter=new TextBox{MinWidth=240,Margin=new Thickness(4,5,4,10),ToolTip="Filter by name, ID, category or description"};
  page.Children.Add(new TextBlock{Text="Search",Margin=new Thickness(4,0,4,0)});
  page.Children.Add(filter);
  var actions=new StackPanel{Orientation=Orientation.Horizontal};
  var list=new StackPanel();
  var count=new TextBlock{Margin=new Thickness(4,2,4,10)};
  void Run(Action action)
  {
   if(_busy||!ReferenceEquals(session,_session))return;
   try{action();Status.Text=UiStrings.LocalizeText("Account changes applied in memory. Press Save to keep them.");}
   catch(Exception ex){Error(ex);}
   Render();
  }
  foreach(bool unlock in new[]{true,false})
  {
   var button=new Button{Content=UiStrings.Get(unlock?"common.unlock_all":"common.lock_all"),Style=(Style)FindResource("Primary"),IsEnabled=available,Margin=new Thickness(4,2,8,10)};
   button.Click+=(_,_)=>Run(()=>{if(titles)session.ApplyAllPlayerTitles(unlock);else session.ApplyAllPlayerGuides(unlock);});
   actions.Children.Add(button);
  }
  page.Children.Add(actions);
  page.Children.Add(count);
  page.Children.Add(list);
  CheckBox Toggle(string label,bool value,Action<bool> apply)
  {
   var check=new CheckBox{Content=label,IsChecked=value,IsEnabled=available,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(10,3,3,3)};
   check.SetResourceReference(Control.ForegroundProperty,"Text");
   check.Click+=(_,_)=>Run(()=>apply(check.IsChecked==true));
   return check;
  }
  void Render()
  {
   list.Children.Clear();string search=filter.Text.Trim();int shown=0,total=0;
   bool Matches(params string[] values)=>search.Length==0||values.Any(s=>s.Contains(search,StringComparison.OrdinalIgnoreCase));
   if(titles)
   {
    var rows=session.ReadPlayerTitles();total=rows.Count;
    foreach(var row in rows)
    {
     if(!Matches(row.Id,row.Name,row.Description,row.UnlockedDescription))continue;
     shown++;
     var panel=new StackPanel();
     var top=new DockPanel();
     var check=Toggle("Unlocked",row.Unlocked,v=>session.ApplyPlayerTitle(row.Id,v));DockPanel.SetDock(check,Dock.Right);top.Children.Add(check);
     top.Children.Add(new TextBlock{Text=row.Name,FontSize=16,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});
     panel.Children.Add(top);
     panel.Children.Add(new TextBlock{Text=row.Id,Margin=new Thickness(0,3,0,5),Opacity=.65});
     panel.Children.Add(new TextBlock{Text=row.Description,TextWrapping=TextWrapping.Wrap});
     if(row.Unlocked&&!string.IsNullOrEmpty(row.UnlockedDescription))panel.Children.Add(new TextBlock{Text=row.UnlockedDescription,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,0),Opacity=.8});
     list.Children.Add(new Border{Style=(Style)FindResource("Card"),Child=panel});
    }
   }
   else
   {
    var rows=session.ReadPlayerGuides();total=rows.Count;
    foreach(var group in rows.Where(r=>Matches(r.Id,r.Name,r.Category)).GroupBy(r=>r.Category))
    {
     list.Children.Add(new TextBlock{Text=group.Key,FontSize=17,FontWeight=FontWeights.SemiBold,Margin=new Thickness(4,12,4,6)});
     var groupPanel=new StackPanel();
     foreach(var row in group)
     {
      shown++;
      var line=new DockPanel{Margin=new Thickness(0,4,0,4)};
      var toggles=new StackPanel{Orientation=Orientation.Horizontal};
      toggles.Children.Add(Toggle("Seen",row.Seen,v=>session.ApplyPlayerGuide(row.Id,v,row.Unlocked)));
      toggles.Children.Add(Toggle("Unlocked",row.Unlocked,v=>session.ApplyPlayerGuide(row.Id,row.Seen,v)));
      DockPanel.SetDock(toggles,Dock.Right);line.Children.Add(toggles);
      var icon=LoadIcon(row.IconKey+".png");
      if(icon!=null){var image=new Image{Source=icon,Width=32,Height=32,Margin=new Thickness(0,0,10,0)};DockPanel.SetDock(image,Dock.Left);line.Children.Add(image);}
      var text=new StackPanel();text.Children.Add(new TextBlock{Text=row.Name,TextWrapping=TextWrapping.Wrap,FontWeight=FontWeights.SemiBold});text.Children.Add(new TextBlock{Text=row.Id,Opacity=.6,FontSize=11});
      line.Children.Add(text);groupPanel.Children.Add(line);
     }
     list.Children.Add(new Border{Style=(Style)FindResource("Card"),Child=groupPanel});
    }
   }
   count.Text=string.Format(UiStrings.LocalizeText("{0} of {1} · Changes apply immediately in memory. Bulk actions include filtered-out database entries; unknown IDs are preserved."),shown,total);
   if(total==0)list.Children.Add(new TextBlock{Text="The knowledge database is unavailable.",Margin=new Thickness(4)});
  }
  filter.TextChanged+=(_,_)=>Render();
  Render();
  return new ScrollViewer{Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
 }
}
