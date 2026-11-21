using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NMSE.Data;
using NMSE.Models;
using NMSE.Next.Engine;

namespace NMSE.Next.Desktop;

public partial class MainWindow
{
 static readonly InventoryKind ToolInventoryKind = new("Weapon", "Personal", true, true);
 InventoryKind ExosuitInventoryKind => _showingStarships
  ? new(SaveSession.ShipInventoryOwner(_selectedShip?.Ownership), _exoInventoryKey == "Inventory_TechOnly" ? "Ship" : "ShipCargo", _exoInventoryKey == "Inventory_TechOnly", _exoInventoryKey == "Inventory_TechOnly")
  : _showingExocraft
  ? new(CurrentExoInventory is {} inventory ? SaveSession.VehicleInventoryOwner(inventory) : "Vehicle", "Vehicle", _exoInventoryKey == "Inventory_TechOnly", _exoInventoryKey == "Inventory_TechOnly")
  : _exoInventoryKey == "Inventory_TechOnly"
  ? new("Suit", "Personal", true, true) : new("Suit", "PersonalCargo", false, false);
 (SaveSession Session, JsonObject Item, InventoryKind Kind)? _inventoryClipboard;
 readonly System.Runtime.CompilerServices.ConditionalWeakTable<SaveSession, Dictionary<InventoryKind, GameItem[]>> _pickerCatalog = new();
 readonly System.Runtime.CompilerServices.ConditionalWeakTable<JsonObject, HashSet<(int,int)>> _pinnedSlots = new();

 void WireToolTile(object sender, RoutedEventArgs e)
 {
  var button = (Button)sender;
  if (button.Tag is SlotView slot)
   WireInventoryTile(button, slot, ToolInventoryKind, CommitDraft, RenderInventory,
    () => SelectSlot(button, new RoutedEventArgs()), () => ((Border)ToolItemPicker.Content).BringIntoView());
 }

 internal void WireInventoryTile(Button button, SlotView slot, InventoryKind kind, Func<bool> commit,
  Action refresh, Action select, Action revealPicker)
 {
  if (slot.Inventory == null || _session.SourcePath == null) return;
  var menu = new ContextMenu();
  menu.SetResourceReference(Control.BackgroundProperty, "Surface");
  menu.SetResourceReference(Control.ForegroundProperty, "Text");
  menu.Items.Add(new MenuItem());
  button.ContextMenu = menu;
  menu.Opened += (_, _) =>
  {
   if (_busy || !commit()) { menu.IsOpen = false; return; }
   select();
   PopulateInventoryMenu(menu, slot, kind, commit, refresh, revealPicker);
  };
 }

 void PopulateInventoryMenu(ContextMenu menu, SlotView slot, InventoryKind kind, Func<bool> commit,
  Action refresh, Action revealPicker)
 {
  menu.Items.Clear();
  var inventory = slot.Inventory!;
  var pinned = _pinnedSlots.GetOrCreateValue(inventory);
  var position = (slot.X, slot.Y);
  bool occupied = slot.Data != null;
  bool Match(JsonObject item) => SaveSession.AtPosition(item.GetObject("Index"), slot.X, slot.Y);
  void Add(string label, Action action, bool enabled = true)
  {
   var entry = new MenuItem { Header = UiStrings.LocalizeText(label), IsEnabled = enabled };
   entry.SetResourceReference(Control.ForegroundProperty, "Text");
   entry.Click += (_, _) => action();
   menu.Items.Add(entry);
  }
  void Edit(string label, Action<JsonObject> edit, bool enabled = true) =>
   Add(label, () => RunInventoryAction(commit, refresh, () => _session.EditInventory(inventory, edit)), enabled);
  void Separate() => menu.Items.Add(new Separator());

  Add(occupied ? "Replace Item…" : "Add Item…", revealPicker, slot.Activated);
  if (occupied)
   Edit("Remove Item", draft => SaveSession.RemoveEntries(draft.GetArray("Slots")!, Match));
  Separate();
  if (kind.CanDisable)
  {
   Edit(slot.Activated ? "Disable Slot" : "Enable Slot", draft =>
   {
    SaveSession.SetSlotEnabled(draft, slot.X, slot.Y, !slot.Activated);
    if (slot.Supercharged) SaveSession.SetSupercharge(draft, slot.X, slot.Y, false);
   }, !occupied);
   Edit("Enable All Slots", draft =>
   {
    int width = Math.Clamp(Int(draft, "Width"), 1, 20), height = Math.Clamp(Int(draft, "Height"), 1, 20);
    for (int y = 0; y < height; y++)
     for (int x = 0; x < width; x++) SaveSession.SetSlotEnabled(draft, x, y, true);
   });
  }
  if (kind.Group is "PersonalCargo" or "ShipCargo")
   Add(pinned.Contains(position) ? "Unpin Slot" : "Pin Slot", () =>
   {
    if (!pinned.Remove(position)) pinned.Add(position);
    Status.Text = UiStrings.LocalizeText("Slot pin updated for this session.");
   }, slot.Activated);
  if (kind.Supercharge)
  {
   Edit(slot.Supercharged ? "Remove Supercharge" : "Supercharge Slot",
    draft => SaveSession.SetSupercharge(draft, slot.X, slot.Y, !slot.Supercharged), slot.Activated);
   Edit("Supercharge All Enabled Slots", draft =>
   {
    foreach (var cell in BuildSlotModels(draft).Items.Where(cell => cell.Activated && !cell.Supercharged))
     SaveSession.SetSupercharge(draft, cell.X, cell.Y, true);
   });
   Edit("Remove All Supercharged Slots", draft =>
   {
    if (draft.GetArray("SpecialSlots") is {} special) SaveSession.RemoveEntries(special, SaveSession.IsSupercharge);
   });
  }
  Edit("Repair Slot", draft => _session.RepairInventory(draft, kind, Match), occupied);
  Edit(kind.Technology ? "Fix All Damaged" : "Repair All Slots",
   draft => _session.RepairInventory(draft, kind, _ => true));
  bool CanFill(JsonObject item) => Int(item, "Amount") >= 0 && Int(item, "MaxAmount") > Int(item, "Amount")
   && (kind.Technology ? _session.Items.GetItem((item.GetString("Id") ?? "").Split('#')[0])?.IsChargeable == true
    : item.GetObject("Type")?.GetString("InventoryType") != "Technology");
  void Fill(JsonObject draft, Func<JsonObject, bool> match)
  {
   var items = draft.GetArray("Slots")!;
   for (int i = 0; i < items.Length; i++)
    if (items.GetObject(i) is {} item && match(item) && CanFill(item))
     item.Set("Amount", Int(item, "MaxAmount"));
  }
  Edit(kind.Technology ? "Recharge Slot" : "Fill Stack", draft => Fill(draft, Match), slot.Data is {} data && CanFill(data));
  Edit(kind.Technology ? "Recharge All Technology" : "Refill All Stacks", draft => Fill(draft, _ => true));
  if (!kind.Technology)
  {
   bool IsStack(JsonObject item) => Int(item, "Amount") > 1 && Int(item, "MaxAmount") > 1;
   void Halve(JsonObject draft, Func<JsonObject, bool> match)
   {
    var items = draft.GetArray("Slots")!;
    for (int i = 0; i < items.Length; i++)
     if (items.GetObject(i) is {} item && match(item) && IsStack(item))
      item.Set("Amount", Int(item, "Amount") / 2);
   }
   Edit("Half Stack", draft => Halve(draft, Match), occupied && slot.Data is {} stack && IsStack(stack));
   bool hasFullStacks = inventory.GetArray("Slots") is {} currentSlots
    && Enumerable.Range(0, currentSlots.Length).Select(currentSlots.GetObject).Any(item => item != null
     && Int(item, "MaxAmount") > 1 && Int(item, "Amount") == Int(item, "MaxAmount"));
   Edit("Half All Full Stacks", draft => Halve(draft,
    item => Int(item, "MaxAmount") > 1 && Int(item, "Amount") == Int(item, "MaxAmount")), hasFullStacks);
  }
  Separate();
  Add("Copy Item", () =>
  {
   if (commit() && slot.Data != null) _inventoryClipboard = (_session, slot.Data.DeepClone(), kind);
  }, occupied);
  if (_inventoryClipboard is {} copy && ReferenceEquals(copy.Session, _session) && slot.Activated
   && (copy.Kind == kind || (!copy.Kind.Technology && !kind.Technology && copy.Kind.Group == kind.Group)))
   Edit(occupied ? "Paste and Replace Item" : "Paste Item",
    draft => SaveSession.PutInventoryItem(draft, slot.X, slot.Y, copy.Item.DeepClone()));
  if (occupied)
  {
   if (_session.BestInventoryRoll(slot.Data!) is {} bestId)
    Edit("Set Best Seed", draft =>
    {
     var slots = draft.GetArray("Slots")!;
     for (int i = 0; i < slots.Length; i++)
      if (slots.GetObject(i) is {} item && Match(item)) item.Set("Id", bestId);
    });
   var transfers = _session.TechnologyTransfers(inventory, slot.X, slot.Y, kind.Technology);
   if (transfers.Count > 0)
   {
    var transferMenu = new MenuItem { Header = UiStrings.LocalizeText(kind.Technology ? "Move Technology" : "Unpack Technology") };
    if (kind.Technology) transferMenu.ToolTip = UiStrings.LocalizeText("Moves the upgrade to the selected inventory as packaged technology.");
   foreach (var (name, move) in transfers)
    {
     var target = new MenuItem { Header = UiStrings.LocalizeText(name) };
     target.Click += (_, _) => RunInventoryAction(commit, refresh, move);
     transferMenu.Items.Add(target);
    }
    menu.Items.Add(transferMenu);
   }
  }
  if (!kind.Technology && occupied && kind.Group is "PersonalCargo" or "ShipCargo" or "FreighterCargo" or "Vehicle")
  {
   Separate();
   foreach (string destination in new[] { "Chests", "Starship", "Freighter" })
   {
    if ((destination == "Starship" && kind.Group == "ShipCargo") ||
        (destination == "Freighter" && kind.Group == "FreighterCargo")) continue;
    Add(UiStrings.Format("inventory.menu.auto_stack", UiStrings.LocalizeText(destination)),
     () => RunInventoryAction(commit, refresh, () => _session.StackInventoryItem(inventory, destination, slot.X, slot.Y, pinned)),
     !pinned.Contains(position));
   }
  }
 }

 sealed record FilterChoice(string Value, string Label);

 void RunInventoryAction(Func<bool> commit, Action refresh, Action action)
 {
  if (_busy || !commit()) return;
  try
  {
   action();
   refresh();
   Status.Text = UiStrings.LocalizeText("Inventory updated in memory. Press Save to keep the changes.");
  }
  catch (Exception ex) { Error(ex); }
 }

 internal Border BuildItemPicker(SlotView? slot, InventoryKind kind, Func<bool> commit, Action refresh,
  Func<GameItem, bool>? additionalFilter = null)
 {
  var fields = new StackPanel();
  var card = new Border { Style = (Style)FindResource("Card"), Child = fields };
  fields.Children.Add(new TextBlock { Text = UiStrings.Get("inventory.item_picker"), Style = (Style)FindResource("Heading"), FontSize = 17 });
  var preview = new Image { Height = 72, Margin = new Thickness(4), Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Center };
  fields.Children.Add(preview);
  if (slot?.Inventory is not {} inventory || !slot.Activated || _session.SourcePath == null)
  {
   fields.Children.Add(new TextBlock { Text = UiStrings.Get("inventory.select_enabled_slot"), TextWrapping = TextWrapping.Wrap });
   return card;
  }

  void Field(string label, FrameworkElement control)
  {
   fields.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("Label") });
   fields.Children.Add(control);
  }
  var search = new TextBox();
  var type = new ComboBox();
  var category = new ComboBox();
  var choices = new ComboBox { DisplayMemberPath = nameof(GameItem.Name) };
  Field("Search", search);
  Field("Type", type);
  Field("Category", category);
  Field("Item", choices);
  var apply = new Button { Content = slot.Data == null ? "Add Item" : "Replace Item", IsEnabled = false };
  fields.Children.Add(apply);
  var available = _pickerCatalog.GetOrCreateValue(_session);
  if (!available.TryGetValue(kind, out var items))
   available[kind] = items = _session.Items.Items.Values.Where(kind.Accepts).ToArray();
  if (additionalFilter != null)
   items = items.Where(additionalFilter).ToArray();
  items = items.OrderBy(item => item.Name).ToArray();
  const string all = "";
  type.DisplayMemberPath = nameof(FilterChoice.Label); type.SelectedValuePath = nameof(FilterChoice.Value);
  category.DisplayMemberPath = nameof(FilterChoice.Label); category.SelectedValuePath = nameof(FilterChoice.Value);
  type.ItemsSource = items.Select(item => item.ItemType).Where(value => value.Length > 0).Distinct().Order()
   .Select(value => new FilterChoice(value, UiStrings.LocalizeText(value))).Prepend(new FilterChoice(all, UiStrings.LocalizeText("All"))).ToArray();
  type.SelectedIndex = 0;
  void Filter()
  {
   string query = search.Text.Trim().TrimStart('^');
   string selectedType = type.SelectedValue as string ?? all;
   string selectedCategory = category.SelectedValue as string ?? all;
   var filtered = items.Where(item => (selectedType.Length == 0 || item.ItemType == selectedType)
    && (selectedCategory.Length == 0 || item.Category == selectedCategory)
    && (item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || item.Id.Contains(query, StringComparison.OrdinalIgnoreCase)));
   var selected = choices.SelectedItem;
   choices.ItemsSource = filtered.ToArray();
   choices.SelectedItem = selected;
  }
  void SetCategories()
  {
   string selectedType = type.SelectedValue as string ?? all;
   category.ItemsSource = items.Where(item => selectedType.Length == 0 || item.ItemType == selectedType)
    .Select(item => item.Category).Where(value => value.Length > 0).Distinct().Order()
    .Select(value => new FilterChoice(value, UiStrings.LocalizeText(value))).Prepend(new FilterChoice(all, UiStrings.LocalizeText("All"))).ToArray();
   category.SelectedIndex = 0;
   Filter();
  }
  SetCategories();
  search.TextChanged += (_, _) => Filter();
  type.SelectionChanged += (_, _) => SetCategories();
  category.SelectionChanged += (_, _) => Filter();
  choices.SelectionChanged += (_, _) =>
  {
   var item = choices.SelectedItem as GameItem;
   preview.Source = LoadIcon(item?.Icon);
   preview.Visibility = preview.Source == null ? Visibility.Collapsed : Visibility.Visible;
   apply.IsEnabled = item != null;
  };
  apply.Click += (_, _) =>
  {
   if (choices.SelectedItem is GameItem item)
    RunInventoryAction(commit, refresh, () => _session.EditInventory(inventory,
     draft => SaveSession.PutInventoryItem(draft, slot.X, slot.Y, SaveSession.NewInventoryItem(kind, item))));
  };
  return card;
 }
}
