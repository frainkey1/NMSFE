using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using NMSE.Core;
using NMSE.Data;
using NMSE.Models;
using NMSE.Next.Engine;

namespace NMSE.Next.Desktop;

public partial class MainWindow
{
 sealed record ShipInventoryEntry(string Name, JsonObject Ownership, int Index, bool Occupied);

 bool _showArchive;
 bool _showShipCustomisation;
 bool _showCorvetteCustomisation;
 string _corvetteInventoryKey = "Inventory";
 bool _showingShipCustomisation => _showingStarships ? _showShipCustomisation : _showCorvetteCustomisation;
 string _shipInventoryKey = "Inventory";
 ShipInventoryEntry? _selectedShip;
 int _shipSelectionPage=-1;

 void ClearShipInventoryState()
 {
  _showArchive = false;
  _showShipCustomisation = false;
  _showCorvetteCustomisation = false;
  _corvetteInventoryKey = "Inventory";
  _shipInventoryKey = "Inventory";
  _selectedShip = null;
  _shipSelectionPage = -1;
 }

 string[] SelectedCorvetteBaseJsonPath()
 {
  var bases=_session.Player?.GetArray("PersistentPlayerBases");
  int baseIndex=_selectedShip==null || _showArchive?-1:StarshipLogic.FindCorvetteBaseIndex(bases,_selectedShip.Index);
  return baseIndex<0?["PlayerStateData","PersistentPlayerBases"]:["PlayerStateData","PersistentPlayerBases",$"[{baseIndex}]"];
 }

 string[] SelectedShipCustomisationJsonPath()
 {
  int ccdIndex=_selectedShip==null||_showArchive?-1:StarshipLogic.ShipIndexToCcdIndex(_selectedShip.Index);
  return ccdIndex<0?["PlayerStateData","CharacterCustomisationData"]:["PlayerStateData","CharacterCustomisationData",$"[{ccdIndex}]"];
 }

 JsonObject? SelectedShipInventory()
 {
  var ship = _selectedShip?.Ownership;
  if (ship == null) return null;
  if (_exoInventoryKey == "Inventory_TechOnly")
   return ship.GetObject("Inventory_TechOnly") ?? ship.GetObject("Inventory_Tech");
  return _showArchive
   ? ship.GetObject("Inventory_Cargo") ?? ship.GetObject("Inventory")
   : ship.GetObject("Inventory") ?? ship.GetObject("Inventory_Cargo");
 }

 IReadOnlyList<ShipInventoryEntry> GetShipInventoryEntries(bool archived)
 {
  var entries = new List<ShipInventoryEntry>();
  var ships = _session.Player?.GetArray(archived ? "ArchivedShipOwnership" : "ShipOwnership");
  if (ships == null) return entries;
  for (int i = 0; i < ships.Length; i++)
  {
   var record = ships.GetObject(i);
   if (record == null) continue;
   var ownership = archived ? record.GetObject("Ownership") : record;
   if (ownership == null || (archived && !StarshipLogic.IsArchivedShipSlotOccupied(record))) continue;
   if (!archived)
   {
    bool occupied = false;
    try
    {
     var seed = ownership.GetObject("Resource")?.GetArray("Seed");
     occupied = seed != null && seed.Length > 0 && seed.GetBool(0);
    }
    catch { }
    if (!occupied)
    {
     entries.Add(new ShipInventoryEntry(UiStrings.Format("companion.empty_slot_number", i + 1), ownership, i, false));
     continue;
    }
   }
   string filename=ownership.GetObject("Resource")?.GetString("Filename")??"";
   bool isCorvette=StarshipLogic.IsCorvette(filename);
   if(isCorvette!=_showingCorvettes)continue;
   var name = archived ? record.GetString("ArchivedName") : ownership.GetString("Name");
   if (string.IsNullOrWhiteSpace(name)) name = SectionCatalog.Caption(ownership, i);
   entries.Add(new ShipInventoryEntry(UiStrings.LocalizeText(name), ownership, i, true));
  }
  return entries;
 }

 Border BuildShipSelector()
 {
  IReadOnlyList<ShipInventoryEntry> owned = GetShipInventoryEntries(false);
  IReadOnlyList<ShipInventoryEntry> archived = GetShipInventoryEntries(true);
  var tabs = new UniformGrid { Columns = 2, Margin = new Thickness(0,0,0,4) };
  var ownedTab = new Button { Content = UiStrings.Get("ui.starship.owned"), Padding = new Thickness(5,6,5,6) };
  var archiveTab = new Button { Content = UiStrings.Get("ui.starship.archive"), Padding = new Thickness(5,6,5,6) };
  tabs.Children.Add(ownedTab);
  tabs.Children.Add(archiveTab);

  var list = new ListBox { DisplayMemberPath = nameof(ShipInventoryEntry.Name) };
  list.SetResourceReference(Control.BackgroundProperty, "Input");
  list.SetResourceReference(Control.ForegroundProperty, "Text");
  list.SetResourceReference(Control.BorderBrushProperty, "Edge");
  ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
  var empty = new TextBlock { Text = UiStrings.LocalizeText(_showingCorvettes ? "No Corvettes" : "No ships"), Margin = new Thickness(6) };
  var listArea = new Grid();
  listArea.Children.Add(list);
  listArea.Children.Add(empty);
  var selector = new DockPanel();
  DockPanel.SetDock(tabs, Dock.Top);
  selector.Children.Add(tabs);
  if(_showingCorvettes)
  {
   var sharedSlots=new TextBlock{Text=UiStrings.Format("editor.shared_ship_slots",12),Margin=new Thickness(5,5,5,4),Foreground=(Brush)FindResource("Secondary")};
   DockPanel.SetDock(sharedSlots,Dock.Top);selector.Children.Add(sharedSlots);
  }
  selector.Children.Add(listArea);

  bool binding = false;
  void UpdateList()
  {
   owned = GetShipInventoryEntries(false); archived = GetShipInventoryEntries(true);
   var entries = _showArchive ? archived : owned;
   binding = true;
   _selectedShip = (_shipSelectionPage==_inventoryPage ? entries.FirstOrDefault(ship => ReferenceEquals(ship.Ownership, _selectedShip?.Ownership)) : null)
    ?? (!_showArchive ? entries.FirstOrDefault(ship => ship.Occupied && ship.Index == PrimaryShipIndex()) : null)
    ?? (!_showArchive ? entries.FirstOrDefault(ship => ship.Occupied && !string.IsNullOrWhiteSpace(ship.Ownership.GetString("Name"))) : null)
    ?? (!_showArchive ? entries.FirstOrDefault(ship => ship.Occupied) : null)
    ?? entries.FirstOrDefault();
   list.ItemsSource = entries;
   list.SelectedItem = _selectedShip;
   _shipSelectionPage = _inventoryPage;
   binding = false;
   empty.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
   foreach (var (tab, selected) in new[] { (ownedTab, !_showArchive), (archiveTab, _showArchive) })
   {
    tab.SetResourceReference(Control.BackgroundProperty, selected ? "AccentSurface" : "Surface");
    tab.SetResourceReference(Control.ForegroundProperty, selected ? "OnAccent" : "Text");
   }
  }

  void SwitchGroup(bool archive)
  {
   if (_showArchive == archive || !CommitShipDetails() || !CommitExosuitPage()) return;
   _showArchive = archive;
   _exoSelected = null;
   UpdateList();
   RefreshShipDetails();
   RenderExosuitInventory();
  }

  ownedTab.Click += (_, _) => SwitchGroup(false);
  archiveTab.Click += (_, _) => SwitchGroup(true);
  list.SelectionChanged += (_, _) =>
  {
   if (binding) return;
   if (!CommitShipDetails() || !CommitExosuitPage())
   {
    binding = true;
    list.SelectedItem = _selectedShip;
    binding = false;
    return;
   }
   _selectedShip = list.SelectedItem as ShipInventoryEntry;
   RefreshShipDetails();
   _exoSelected = null;
   if(_showingShipCustomisation){_exoContent=null;ShowExosuitPage(_inventoryPage);}
   else RenderExosuitInventory();
  };
  UpdateList();
  RefreshShipDetails();
  _refreshShipList = UpdateList;

  var measure = new TextBlock { FontFamily = FontFamily, FontSize = FontSize };
  double width = 138;
  foreach (var ship in owned.Concat(archived))
  {
   measure.Text = ship.Name;
   measure.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
   width = Math.Max(width, Math.Ceiling(measure.DesiredSize.Width) + 34);
  }
  return new Border { Width = width, Style = (Style)FindResource("Card"), Padding = new Thickness(5), Child = selector };
 }
}
