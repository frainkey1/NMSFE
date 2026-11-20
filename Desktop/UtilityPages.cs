using System.IO;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Win32;
using NMSE.Core;
using NMSE.Data;
using NMSE.Models;
using NMSE.Next.Engine;
namespace NMSE.Next.Desktop;

internal sealed record DatabaseSearchRow(GameItem Item,string Name,string Category,string Id,string Source);

public partial class MainWindow
{
 readonly Dictionary<int,FrameworkElement> _utilityPages=[];
 readonly Dictionary<PropertyInfo,TextBox> _exportFields=[];
 TextBox? _rawText; string _rawBaseline=""; RawNode? _rawSelected;
 TreeView? _rawTree; TextBlock? _rawHint;
 bool UtilityPagesHavePendingChanges=>_rawText!=null&&_rawText.Text!=_rawBaseline;
 sealed record RawNode(object? Value,object? Parent,object? Key,string Path);

 void ClearUtilityPageState(){_utilityPages.Clear();_rawText=null;_rawSelected=null;_rawTree=null;_rawHint=null;_rawBaseline="";_exportFields.Clear();}
 bool CommitUtilityPages()
 {
  if(!UtilityPagesHavePendingChanges)return true;
  if(_busy)return false;
  try{if(_rawSelected?.Parent==null)throw new InvalidOperationException("Select a field first.");_session.ApplyRawSubtree(_rawSelected.Parent,_rawSelected.Key!,_rawText!.Text);_rawBaseline=_rawText.Text;RebuildRawTree();Status.Text=UiStrings.LocalizeText("JSON edits applied in memory. Save to keep them.");return true;}catch(Exception ex){Error(ex);return false;}
 }
 void ShowUtilityPage(int page)
 {
  if(!_utilityPages.TryGetValue(page,out var content))
  {
   content=page switch{MainPageCatalog.ExportSettings=>BuildExportPage(),MainPageCatalog.RawJsonEditor=>BuildRawPage(),MainPageCatalog.DatabaseSearch=>BuildDatabasePage(),_=>throw new ArgumentOutOfRangeException(nameof(page))};_utilityPages[page]=content;
  }
  MigrationPage.Child=content;MigrationPage.Visibility=Visibility.Visible;Workspace.Visibility=Visibility.Collapsed;
 }
 internal Button JsonRedirectButton(string label,string tooltip,Action navigate)
 {
  var button=new Button{Content=label,ToolTip=tooltip,Padding=new Thickness(8,4,8,4),Margin=new Thickness(3,2,3,2),MinWidth=42};
  button.Click+=(_,_)=>{if(!_busy)navigate();};
  return button;
 }
 internal string[] RawPathForSection(SectionSpec section)
 {
  if(section.Root=="Account")return [];
  string path=section.Path.Split('|',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries)
   .FirstOrDefault(candidate=>_session.Data.GetValue(candidate)!=null)
   ??section.Path.Split('|',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).First();
  var parts=new List<string>();
  if(section.Root=="Player")parts.Add("PlayerStateData");
  parts.AddRange(ParseRawPath(path));
  return parts.ToArray();
 }
 static IEnumerable<string> ParseRawPath(string path)
 {
  foreach(string part in path.Replace("[",".[",StringComparison.Ordinal).Split('.',StringSplitOptions.RemoveEmptyEntries))
   yield return part;
 }
 StackPanel UtilityPanel(string heading,string hint)
 {
  var p=new StackPanel();p.Children.Add(new TextBlock{Text=heading,Style=(Style)FindResource("Heading")});p.Children.Add(new TextBlock{Text=hint,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(5,4,5,14)});return p;
 }
 Button UtilityButton(string text,Action click){var b=new Button{Content=text,Margin=new Thickness(3,6,3,6)};b.Click+=(_,_)=>{if(!_busy)click();};return b;}
 FrameworkElement BuildExportPage()
 {
  var p=UtilityPanel("Export Settings","Application export profiles control file extensions and suggested names. They are separate from game saves. Apply updates this session; export a profile to keep it for later, and import it next time.");
  foreach(var group in typeof(ExportConfig).GetProperties().Where(x=>x.PropertyType==typeof(string)&&x.CanWrite).GroupBy(x=>x.Name.EndsWith("Ext")?"File extensions":"Naming templates"))
  {
   var fields=new StackPanel();fields.Children.Add(new TextBlock{Text=UiStrings.LocalizeText(group.Key),FontSize=17,Margin=new Thickness(4,4,4,8)});
   foreach(var prop in group){var row=new Grid();row.ColumnDefinitions.Add(new(){Width=new GridLength(230)});row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.Children.Add(new TextBlock{Text=UiStrings.LocalizeText(System.Text.RegularExpressions.Regex.Replace(prop.Name,"([a-z])([A-Z])","$1 $2")),VerticalAlignment=VerticalAlignment.Center});var t=new TextBox{Text=(string?)prop.GetValue(ExportConfig.Instance)??"",MinWidth=200};_exportFields[prop]=t;Grid.SetColumn(t,1);row.Children.Add(t);fields.Children.Add(row);}p.Children.Add(new Border{Style=(Style)FindResource("Card"),Child=fields});
  }
  var buttons=new WrapPanel();buttons.Children.Add(UtilityButton("Apply settings",()=>{try{ApplyExportFields();Status.Text=UiStrings.LocalizeText("Export settings applied for this session.");}catch(Exception ex){Error(ex);}}));
  buttons.Children.Add(UtilityButton("Export profile…",()=>{try{ApplyExportFields();var d=new SaveFileDialog{Filter="Export profile (*.json)|*.json",FileName="NMSE-export-profile.json"};if(d.ShowDialog(this)==true)ExportConfig.Instance.SaveToFile(d.FileName);}catch(Exception ex){Error(ex);}}));
  buttons.Children.Add(UtilityButton("Import profile…",()=>{var d=new OpenFileDialog{Filter="Export profile (*.json)|*.json"};if(d.ShowDialog(this)!=true)return;try{var config=JsonSerializer.Deserialize<ExportConfig>(File.ReadAllText(d.FileName))??throw new InvalidDataException("Invalid export profile.");ValidateExport(config);foreach(var pair in _exportFields)pair.Value.Text=(string?)pair.Key.GetValue(config)??"";ApplyExportFields();Status.Text=UiStrings.LocalizeText("Export profile imported for this session.");}catch(Exception ex){Error(ex);}}));p.Children.Add(buttons);return new ScrollViewer{Content=p,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
 }
 void ValidateExport(ExportConfig config){foreach(var prop in _exportFields.Keys){var value=(string?)prop.GetValue(config);if(string.IsNullOrWhiteSpace(value))throw new ArgumentException(string.Format(UiStrings.LocalizeText("{0} cannot be empty."),prop.Name));if(prop.Name.EndsWith("Ext")&&(!value.StartsWith('.')||value.Length<2||value.Skip(1).Any(c=>!char.IsLetterOrDigit(c))))throw new ArgumentException(string.Format(UiStrings.LocalizeText("{0} must be a dot followed by letters or digits."),prop.Name));}}
 void ApplyExportFields(){var config=new ExportConfig();foreach(var pair in _exportFields)pair.Key.SetValue(config,pair.Value.Text);ValidateExport(config);foreach(var pair in _exportFields)pair.Key.SetValue(ExportConfig.Instance,pair.Value.Text);}
 FrameworkElement BuildRawPage()
 {
  var p=UtilityPanel("Raw JSON Editor","Browse the loaded save, expand a field, and edit a selected subtree. Large branches stay collapsed to keep navigation responsive. JSON is validated before Apply; unknown fields and numeric precision are retained.");
  if(_session.SourcePath==null){p.Children.Add(new TextBlock{Text="Open a save to browse its JSON."});return p;}
  var tools=new WrapPanel();var find=new TextBox{Width=220,ToolTip="Find text in the selected subtree"};tools.Children.Add(find);tools.Children.Add(UtilityButton("Find next",()=>{if(_rawText==null||find.Text.Length==0)return;int at=_rawText.Text.IndexOf(find.Text,Math.Min(_rawText.Text.Length,_rawText.SelectionStart+_rawText.SelectionLength),StringComparison.OrdinalIgnoreCase);if(at<0)at=_rawText.Text.IndexOf(find.Text,StringComparison.OrdinalIgnoreCase);if(at>=0){_rawText.Focus();_rawText.Select(at,find.Text.Length);}else Status.Text=UiStrings.LocalizeText("No matching text in this subtree.");}));tools.Children.Add(UtilityButton("Apply JSON edits",()=>CommitUtilityPages()));p.Children.Add(tools);
  _rawHint=new TextBlock{Text="Select a field to inspect or edit.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(4)};p.Children.Add(_rawHint);
  var split=new Grid{Height=470};split.ColumnDefinitions.Add(new(){Width=new GridLength(0.42,GridUnitType.Star)});split.ColumnDefinitions.Add(new(){Width=new GridLength(0.58,GridUnitType.Star)});
  _rawTree=new TreeView{Margin=new Thickness(3)};_rawText=new TextBox{AcceptsReturn=true,AcceptsTab=true,FontFamily=new FontFamily("Consolas"),TextWrapping=TextWrapping.NoWrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,IsEnabled=false};Grid.SetColumn(_rawText,1);split.Children.Add(_rawTree);split.Children.Add(_rawText);p.Children.Add(split);
  _rawTree.SelectedItemChanged+=(_,e)=>{if(e.NewValue is not TreeViewItem item||item.Tag is not RawNode n)return;if(UtilityPagesHavePendingChanges){_rawHint.Text="Apply the current draft before selecting another subtree.";return;}ShowRawNode(n);};RebuildRawTree();return new ScrollViewer{Content=p,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
 }
 void RebuildRawTree(){if(_rawTree==null)return;_rawTree.Items.Clear();foreach(var key in _session.Data.Names())_rawTree.Items.Add(RawItem(key,new(_session.Data.Get(key),_session.Data,key,"$."+key)));}
 TreeViewItem RawItem(string label,RawNode node)
 {
  var item=new TreeViewItem{Header=label+(node.Value is JsonObject o?$"  {{ {o.Length} fields }}":node.Value is JsonArray a?$"  [ {a.Length} items ]":""),Tag=node};
  if(node.Value is JsonObject or JsonArray){item.Items.Add(new TreeViewItem{Header=UiStrings.Get("ui.raw.expand")});item.Expanded+=(_,e)=>{if(!ReferenceEquals(e.OriginalSource,item)||item.Items.Count!=1||item.Items[0] is not TreeViewItem first||first.Tag!=null)return;item.Items.Clear();if(node.Value is JsonObject obj)foreach(var key in obj.Names())item.Items.Add(RawItem(key,new(obj.Get(key),obj,key,node.Path+"."+key)));else if(node.Value is JsonArray arr)AddRawArrayRange(item,arr,node.Path,0,arr.Length);};}return item;
 }
 void AddRawArrayRange(TreeViewItem parent,JsonArray array,string path,int start,int end)
 {
  if(end-start<=100){for(int i=start;i<end;i++)parent.Items.Add(RawItem($"[{i}]",new(array.Get(i),array,i,$"{path}[{i}]")));return;}
  for(int i=start;i<end;i+=100){int a=i,b=Math.Min(i+100,end);var group=new TreeViewItem{Header=$"[{a}…{b-1}]"};group.Items.Add("Expand…");group.Expanded+=(_,e)=>{if(!ReferenceEquals(e.OriginalSource,group)||group.Tag!=null)return;group.Tag=true;group.Items.Clear();AddRawArrayRange(group,array,path,a,b);};parent.Items.Add(group);}
 }
 void ShowRawNode(RawNode n)
 {
  _rawSelected=n;int budget=12000;bool Fits(object? v){if(--budget<0)return false;if(v is string s&&s.Length>300000)return false;if(v is JsonObject o){foreach(var k in o.Names())if(!Fits(o.Get(k)))return false;}else if(v is JsonArray a){for(int i=0;i<a.Length;i++)if(!Fits(a.Get(i)))return false;}return true;}
  bool fits=Fits(n.Value);_rawBaseline=fits?JsonParser.Serialize(n.Value,true):"";_rawText!.Text=_rawBaseline;_rawText.IsEnabled=fits;_rawHint!.Text=fits?string.Format(UiStrings.LocalizeText("{0} · Changes stay in memory until saved."),n.Path):string.Format(UiStrings.LocalizeText("{0} · Expand this large branch and select a smaller subtree to edit."),n.Path);
 }
 internal void NavigateToRawJsonPath(params string[] pathSegments)
 {
  if(_busy||pathSegments.Length==0||!CommitAllEdits())return;
  ShowUtilityPage(MainPageCatalog.RawJsonEditor);
  if(_rawTree==null)return;
  var segments=new List<string>();
  foreach(string segment in pathSegments)
  {
   if(string.IsNullOrWhiteSpace(segment))continue;
   string expanded=segment.Replace("[",".[",StringComparison.Ordinal);
   segments.AddRange(expanded.Split('.',StringSplitOptions.RemoveEmptyEntries));
  }
  if(segments.FirstOrDefault()=="PlayerStateData"&&_session.Data.Get("PlayerStateData")==null)
  {
   string context=_session.Data.GetValue("ExpeditionContext.PlayerStateData")!=null
    &&(_session.Data.GetValue("BaseContext.PlayerStateData")==null
       ||string.Equals(_session.Data.Get("ActiveContext") as string,"Season",StringComparison.Ordinal))
    ?"ExpeditionContext"
    :_session.Data.GetValue("BaseContext.PlayerStateData")!=null?"BaseContext":"";
   if(context.Length>0)segments.Insert(0,context);
  }
  string target="$";TreeViewItem? current=null;
  foreach(string segment in segments)
  {
   target+=segment.StartsWith("[",StringComparison.Ordinal)?segment:"."+segment;
   if(current==null)
    current=_rawTree.Items.OfType<TreeViewItem>().FirstOrDefault(item=>item.Tag is RawNode node&&node.Path==target);
   else
   {
    current.IsExpanded=true;_rawTree.UpdateLayout();
    current=FindRawChild(current,target,segment);
   }
   if(current==null)
   {
    if(_rawHint!=null)_rawHint.Text=$"Could not find {target} in the loaded save.";
    return;
   }
  }
  if(current==null){if(_rawHint!=null)_rawHint.Text="Choose a JSON destination.";return;}
  current.IsSelected=true;current.BringIntoView();_rawTree.UpdateLayout();
 }
 TreeViewItem? FindRawChild(TreeViewItem parent,string target,string segment)
 {
  foreach(var child in parent.Items.OfType<TreeViewItem>())
   if(child.Tag is RawNode node&&node.Path==target)return child;
  if(!segment.StartsWith("[",StringComparison.Ordinal)||!int.TryParse(segment.AsSpan(1,segment.Length-2),NumberStyles.Integer,CultureInfo.InvariantCulture,out int index))return null;
  foreach(var group in parent.Items.OfType<TreeViewItem>())
  {
   string header=group.Header?.ToString()??"";
   int dots=header.IndexOf('…');
   if(dots<0||!int.TryParse(header.AsSpan(1,dots-1),NumberStyles.Integer,CultureInfo.InvariantCulture,out int first)
      ||!int.TryParse(header.AsSpan(dots+1,header.Length-dots-2),NumberStyles.Integer,CultureInfo.InvariantCulture,out int last)
      ||index<first||index>last)continue;
   group.IsExpanded=true;_rawTree?.UpdateLayout();
   return group.Items.OfType<TreeViewItem>().FirstOrDefault(item=>item.Tag is RawNode child&&child.Path==target);
  }
  return null;
 }
 FrameworkElement BuildDatabasePage()
 {
  const string allSources="";
  var root=new Grid{Margin=new Thickness(6)};
  root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
  root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
  root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
  root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
  root.Children.Add(UtilityPanel("Database Search","Search the bundled game item database by name, ID, category, or description. This reference page never changes your save."));

  var toolbar=new Grid{Margin=new Thickness(0,2,0,8)};
  toolbar.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
  toolbar.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
  toolbar.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
  toolbar.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(180)});
  toolbar.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
  var search=new TextBox{ToolTip="Search items",Margin=new Thickness(3),MinWidth=180,VerticalContentAlignment=VerticalAlignment.Center};
  var clear=UtilityButton("×",()=>{search.Clear();search.Focus();});clear.ToolTip="Clear search";clear.MinWidth=34;
  var sourceLabel=new TextBlock{Text="Source:",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(10,0,4,0)};
  var source=new ComboBox{Margin=new Thickness(3),MinWidth=150,VerticalContentAlignment=VerticalAlignment.Center};
  var count=new TextBlock{Margin=new Thickness(12,4,4,0),VerticalAlignment=VerticalAlignment.Center};
  toolbar.Children.Add(search);Grid.SetColumn(clear,1);toolbar.Children.Add(clear);Grid.SetColumn(sourceLabel,2);toolbar.Children.Add(sourceLabel);Grid.SetColumn(source,3);toolbar.Children.Add(source);Grid.SetColumn(count,4);toolbar.Children.Add(count);Grid.SetRow(toolbar,1);root.Children.Add(toolbar);

  var body=new Grid();
  body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(0.55,GridUnitType.Star)});
  body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(0.45,GridUnitType.Star)});
  var resultsPanel=new Grid{Margin=new Thickness(0,0,6,0)};
  resultsPanel.RowDefinitions.Add(new RowDefinition{Height=new GridLength(96)});
  resultsPanel.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
  var preview=new Grid{Margin=new Thickness(3,0,3,7)};
  preview.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(80)});
  preview.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
  var previewIcon=new Image{Width=68,Height=78,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(4)};
  var previewText=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(8,0,4,0)};
  var previewName=new TextBlock{FontSize=17,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap};
  var previewId=new TextBlock{FontSize=12,TextWrapping=TextWrapping.Wrap};
  previewId.SetResourceReference(TextBlock.ForegroundProperty,"Secondary");
  previewText.Children.Add(previewName);previewText.Children.Add(previewId);
  preview.Children.Add(previewIcon);Grid.SetColumn(previewText,1);preview.Children.Add(previewText);
  resultsPanel.Children.Add(new Border{Style=(Style)FindResource("Card"),Child=preview});

  Style HeaderStyle()
  {
   var style=new Style(typeof(DataGridColumnHeader));
   style.Setters.Add(new Setter(Control.BackgroundProperty,new System.Windows.DynamicResourceExtension("Panel")));
   style.Setters.Add(new Setter(Control.ForegroundProperty,new System.Windows.DynamicResourceExtension("Text")));
   style.Setters.Add(new Setter(Control.BorderBrushProperty,new System.Windows.DynamicResourceExtension("Edge")));
   style.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(0,0,1,1)));
   style.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(6,3,6,3)));
   style.Setters.Add(new Setter(Control.FontWeightProperty,FontWeights.SemiBold));
   return style;
  }
  Style RowStyle()
  {
   var style=new Style(typeof(DataGridRow));
   style.Setters.Add(new Setter(Control.BackgroundProperty,new System.Windows.DynamicResourceExtension("Input")));
   style.Setters.Add(new Setter(Control.ForegroundProperty,new System.Windows.DynamicResourceExtension("Text")));
   style.Setters.Add(new Setter(Control.BorderBrushProperty,new System.Windows.DynamicResourceExtension("Edge")));
   style.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(0,0,0,1)));
   style.Triggers.Add(new Trigger{Property=DataGridRow.IsSelectedProperty,Value=true,Setters={new Setter(Control.BackgroundProperty,new System.Windows.DynamicResourceExtension("Selection")),new Setter(Control.ForegroundProperty,new System.Windows.DynamicResourceExtension("Text"))}});
   return style;
  }
  Style CellStyle()
  {
   var style=new Style(typeof(DataGridCell));
   style.Setters.Add(new Setter(Control.ForegroundProperty,new System.Windows.DynamicResourceExtension("Text")));
   style.Setters.Add(new Setter(Control.BorderBrushProperty,new System.Windows.DynamicResourceExtension("Edge")));
   style.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(0,0,1,0)));
   style.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(5,3,5,3)));
   return style;
  }
  var list=new DataGrid{Margin=new Thickness(3),AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,CanUserDeleteRows=false,CanUserReorderColumns=true,SelectionMode=DataGridSelectionMode.Single,SelectionUnit=DataGridSelectionUnit.FullRow,HeadersVisibility=DataGridHeadersVisibility.Column,GridLinesVisibility=DataGridGridLinesVisibility.All,EnableRowVirtualization=true,EnableColumnVirtualization=true,RowStyle=RowStyle(),CellStyle=CellStyle(),ColumnHeaderStyle=HeaderStyle()};
  list.SetResourceReference(Control.BackgroundProperty,"Input");list.SetResourceReference(Control.ForegroundProperty,"Text");list.SetResourceReference(Control.BorderBrushProperty,"Edge");list.SetResourceReference(DataGrid.HorizontalGridLinesBrushProperty,"Edge");list.SetResourceReference(DataGrid.VerticalGridLinesBrushProperty,"Edge");
  list.Columns.Add(new DataGridTextColumn{Header="Name",Binding=new System.Windows.Data.Binding(nameof(DatabaseSearchRow.Name)),Width=new DataGridLength(2,DataGridLengthUnitType.Star)});
  list.Columns.Add(new DataGridTextColumn{Header="Category",Binding=new System.Windows.Data.Binding(nameof(DatabaseSearchRow.Category)),Width=new DataGridLength(1.1,DataGridLengthUnitType.Star)});
  list.Columns.Add(new DataGridTextColumn{Header="ID",Binding=new System.Windows.Data.Binding(nameof(DatabaseSearchRow.Id)),Width=new DataGridLength(1.3,DataGridLengthUnitType.Star)});
  list.Columns.Add(new DataGridTextColumn{Header="Source",Binding=new System.Windows.Data.Binding(nameof(DatabaseSearchRow.Source)),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
  Grid.SetRow(list,1);resultsPanel.Children.Add(list);body.Children.Add(resultsPanel);

  var detail=new Grid{Margin=new Thickness(6,0,0,0)};
  detail.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
  detail.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
  detail.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
  detail.RowDefinitions.Add(new RowDefinition{Height=new GridLength(132)});
  var detailName=new TextBlock{FontSize=19,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(4,2,4,0)};
  var detailId=new TextBlock{FontSize=12,Margin=new Thickness(4,1,4,7)};
  detailId.SetResourceReference(TextBlock.ForegroundProperty,"Secondary");
  var detailHeader=new StackPanel();detailHeader.Children.Add(detailName);detailHeader.Children.Add(detailId);detail.Children.Add(detailHeader);
  var fields=new DataGrid{Margin=new Thickness(3),AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,CanUserDeleteRows=false,HeadersVisibility=DataGridHeadersVisibility.None,GridLinesVisibility=DataGridGridLinesVisibility.All,EnableRowVirtualization=true,RowStyle=RowStyle(),CellStyle=CellStyle()};
  fields.SetResourceReference(Control.BackgroundProperty,"Input");fields.SetResourceReference(Control.ForegroundProperty,"Text");fields.SetResourceReference(Control.BorderBrushProperty,"Edge");fields.SetResourceReference(DataGrid.HorizontalGridLinesBrushProperty,"Edge");fields.SetResourceReference(DataGrid.VerticalGridLinesBrushProperty,"Edge");
  fields.Columns.Add(new DataGridTextColumn{Binding=new System.Windows.Data.Binding(nameof(DatabaseDetailField.Field)),Width=new DataGridLength(0.9,DataGridLengthUnitType.Star)});
  fields.Columns.Add(new DataGridTextColumn{Binding=new System.Windows.Data.Binding(nameof(DatabaseDetailField.Value)),Width=new DataGridLength(1.1,DataGridLengthUnitType.Star)});
  Grid.SetRow(fields,1);detail.Children.Add(fields);
  var descriptionLabel=new TextBlock{Text="Description",FontWeight=FontWeights.SemiBold,Margin=new Thickness(4,7,4,3)};Grid.SetRow(descriptionLabel,2);detail.Children.Add(descriptionLabel);
  var description=new TextBox{IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(3)};
  Grid.SetRow(description,3);detail.Children.Add(description);
  body.Children.Add(new Border{Style=(Style)FindResource("Card"),Child=detail});Grid.SetColumn(body.Children[^1],1);
  Grid.SetRow(body,2);root.Children.Add(body);

  var status=new TextBlock{Margin=new Thickness(4,7,4,0),TextWrapping=TextWrapping.Wrap};Grid.SetRow(status,3);root.Children.Add(status);
  var allItems=_session.Items.Items.Values.OrderBy(x=>x.Name,StringComparer.OrdinalIgnoreCase).ThenBy(x=>x.Id,StringComparer.OrdinalIgnoreCase).ToArray();
  ComboBoxItem[] BuildSourceChoices()=>new[]{new ComboBoxItem{Content=UiStrings.Get("db_search.source_all"),Tag=allSources}}.Concat(allItems.Select(x=>x.ItemType).Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).Select(value=>new ComboBoxItem{Content=UiStrings.LocalizeText(value),Tag=value})).ToArray();
  source.ItemsSource=BuildSourceChoices();
  source.SelectedIndex=0;
  void ShowDetails(GameItem? item)
  {
   if(item==null){previewIcon.Source=null;previewName.Text="No matching items";previewId.Text="";detailName.Text="";detailId.Text="";fields.ItemsSource=null;description.Clear();return;}
   previewIcon.Source=LoadIcon(item.Icon);previewName.Text=UiStrings.LocalizeText(item.Name);previewId.Text=item.Id;
   detailName.Text=UiStrings.LocalizeText(item.Name);detailId.Text=item.Id;
   fields.ItemsSource=typeof(GameItem).GetProperties(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public)
    .Where(property=>property.GetIndexParameters().Length==0&&(property.PropertyType==typeof(string)||property.PropertyType.IsPrimitive||property.PropertyType==typeof(decimal)))
    .Select(property=>{var value=property.GetValue(item);string label=UiStrings.LocalizeText(System.Text.RegularExpressions.Regex.Replace(property.Name,"([a-z])([A-Z])","$1 $2"));if(value is bool flag)return new DatabaseDetailField(label,flag?UiStrings.Get("common.yes"):UiStrings.Get("common.no"));return new DatabaseDetailField(label,Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture)??"");})
    .Where(field=>field.Value.Length>0&&!string.Equals(field.Field,nameof(GameItem.Description),StringComparison.Ordinal)).ToArray();
   description.Text=item.Description;
  }
  void Filter()
  {
   string query=search.Text.Trim();string selectedSource=(source.SelectedItem as ComboBoxItem)?.Tag as string??allSources;
   var results=allItems.Where(item=>(selectedSource==allSources||string.Equals(item.ItemType,selectedSource,StringComparison.OrdinalIgnoreCase))
    &&(query.Length==0||new[]{item.Name,item.Id,item.Category,item.Description,item.ItemType}.Any(value=>value.Contains(query,StringComparison.OrdinalIgnoreCase)))).ToArray();
   list.ItemsSource=results.Select(item=>new DatabaseSearchRow(item,UiStrings.LocalizeText(item.Name),UiStrings.LocalizeText(item.Category),item.Id,UiStrings.LocalizeText(System.Text.RegularExpressions.Regex.Replace(item.ItemType,"(?<=[a-z])(?=[A-Z])"," ")))).ToArray();count.Text=UiStrings.Format("ui.utility.item_count",results.Length.ToString("N0",CultureInfo.CurrentCulture));status.Text=results.Length==0&&allItems.Length>0?UiStrings.Get("db_search.no_results"):UiStrings.Format("ui.utility.items_database",allItems.Length.ToString("N0",CultureInfo.CurrentCulture));
   if(results.Length>0){list.SelectedIndex=0;ShowDetails(results[0]);}else ShowDetails(null);
  }
  list.SelectionChanged+=(_,_)=>ShowDetails((list.SelectedItem as DatabaseSearchRow)?.Item);
  search.TextChanged+=(_,_)=>Filter();source.SelectionChanged+=(_,_)=>Filter();
  if(allItems.Length==0)
  {
   var session=_session;bool loading=false;count.Text="Loading bundled database…";
   root.Loaded+=async(_,_)=>{if(loading)return;if(session.Items.Items.Count!=0){Filter();return;}loading=true;search.IsEnabled=false;try{await Task.Run(session.LoadDatabase);if(ReferenceEquals(_session,session))allItems=session.Items.Items.Values.OrderBy(x=>x.Name,StringComparer.OrdinalIgnoreCase).ThenBy(x=>x.Id,StringComparer.OrdinalIgnoreCase).ToArray();source.ItemsSource=BuildSourceChoices();source.SelectedIndex=0;Filter();}catch(Exception ex){count.Text=string.Format(UiStrings.LocalizeText("Database unavailable: {0}"),ex.Message);}finally{loading=false;search.IsEnabled=true;}};
  }
  else Filter();
  return root;
 }
 private sealed record DatabaseDetailField(string Field,string Value);

}
