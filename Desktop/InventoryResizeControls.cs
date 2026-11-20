using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using NMSE.Core;
using NMSE.Data;
using NMSE.Models;
using NMSE.Next.Engine;

namespace NMSE.Next.Desktop;

public partial class MainWindow
{
 internal FrameworkElement BuildInventoryResizeControls(JsonObject? inventory,int maxColumns,int maxRows,Func<bool> beforeResize,Action refresh,bool compact=false)
 {
  if(inventory==null)return new TextBlock{Text="Grid size unavailable.",Margin=new Thickness(6)};
  var panel=new StackPanel();
  var controls=new WrapPanel{VerticalAlignment=VerticalAlignment.Center};
  if(compact)
   controls.Children.Add(new TextBlock{Text="Resize grid",Style=(Style)FindResource("Heading"),FontSize=14,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(2,0,10,0)});
  else
   panel.Children.Add(new TextBlock{Text="Resize grid",Style=(Style)FindResource("Heading"),FontSize=15,Margin=new Thickness(2,0,2,6)});
  var columns=new TextBox{Text=Int(inventory,"Width").ToString(CultureInfo.InvariantCulture),MinWidth=56,MaxLength=2,ToolTip=UiStrings.Format("inventory.resize.columns_tip",maxColumns)};
  var rows=new TextBox{Text=Int(inventory,"Height").ToString(CultureInfo.InvariantCulture),MinWidth=56,MaxLength=2,ToolTip=UiStrings.Format("inventory.resize.rows_tip",maxRows)};
  controls.Children.Add(new TextBlock{Text=UiStrings.Format("inventory.resize.columns_label",maxColumns),Style=(Style)FindResource("Label"),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(3,0,4,0)});
  controls.Children.Add(columns);
  controls.Children.Add(new TextBlock{Text=UiStrings.Format("inventory.resize.rows_label",maxRows),Style=(Style)FindResource("Label"),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(compact?8:10,0,4,0)});
  controls.Children.Add(rows);
  var apply=new Button{Content="Resize",Style=(Style)FindResource("Primary"),Margin=new Thickness(compact?8:10,0,2,0),MinWidth=compact?72:82};
  apply.Click+=(_,_)=>
  {
   if(!beforeResize())return;
   try
   {
    if(!int.TryParse(columns.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out int width)||width<1||width>maxColumns||
       !int.TryParse(rows.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out int height)||height<1||height>maxRows)
     throw new ArgumentException(UiStrings.Format("inventory.resize.invalid",maxColumns,maxRows));
    _session.ResizeInventory(inventory,width,height);
    refresh();
    Status.Text=UiStrings.Format("inventory.resize.done",width,height);
   }
   catch(Exception ex){Error(ex);}
  };
  controls.Children.Add(apply);panel.Children.Add(controls);
  if(!compact)panel.Children.Add(new TextBlock{Text="Items outside a smaller grid stay saved and reappear if you expand it.",Style=(Style)FindResource("Label"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(3,6,3,0)});
  return new Border{Style=(Style)FindResource("Card"),HorizontalAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(compact?5:8),Margin=new Thickness(0,0,0,compact?3:6),ToolTip=compact?"Items outside a smaller grid stay saved and reappear if you expand it.":null,Child=panel};
 }
}
