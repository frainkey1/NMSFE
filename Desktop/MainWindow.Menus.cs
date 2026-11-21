using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using NMSE.Core;
using NMSE.Data;
using NMSE.IO;
using NMSE.Next.Engine;

namespace NMSE.Next.Desktop;

public partial class MainWindow
{
    const string DefaultLanguageTag = "en-GB";
    readonly string LanguageSettingsPath = Path.Combine(AppContext.BaseDirectory, "language.txt");
    readonly string WatchSettingsPath = Path.Combine(AppContext.BaseDirectory, "save-watch.txt");
    readonly List<MenuItem> _saveRequiredItems = [];
    readonly List<MenuItem> _noConsoleItems = [];
    readonly List<MenuItem> _languageMenuItems = [];
    FileSystemWatcher? _saveWatcher;
    DispatcherTimer? _saveWatchTimer;
    string _languageTag = DefaultLanguageTag;
    string _watchMode = "prompt";
    string? _observedSourceHash;
    ControlTemplate? _headerMenuItemTemplate;

    sealed record BackupChoice(string TransactionDirectory, string FilePath, string Label);

    void InitializeHeaderMenus()
    {
        _languageTag = ReadSetting(LanguageSettingsPath, DefaultLanguageTag);
        if (!LocalisationService.SupportedLanguages.Values.Contains(_languageTag, StringComparer.OrdinalIgnoreCase))
            _languageTag = DefaultLanguageTag;
        _watchMode = ReadSetting(WatchSettingsPath, "prompt").ToLowerInvariant();
        if (_watchMode is not ("prompt" or "auto" or "ignore")) _watchMode = "prompt";
        ApplyLanguagePreference(rebuildMenus: false);
        ApplyHeaderMenuPalette();
        BuildHeaderMenus();
        UpdateHeaderMenuState();

        _saveWatchTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };
        _saveWatchTimer.Tick += async (_, _) =>
        {
            _saveWatchTimer.Stop();
            await EvaluateExternalSaveChange();
        };
    }

    static string ReadSetting(string path, string fallback)
    {
        try { return File.Exists(path) ? File.ReadAllText(path).Trim() : fallback; }
        catch (IOException) { return fallback; }
        catch (UnauthorizedAccessException) { return fallback; }
    }

    void ApplyLanguagePreference(bool rebuildMenus = true)
    {
        UiStrings.SetDirectory(Path.Combine(_session.ResourceRoot, "ui", "lang"));
        if (!UiStrings.Load(_languageTag)) UiStrings.Load(DefaultLanguageTag);
        _session.SetGameLanguage(_languageTag);
        if (rebuildMenus)
        {
            BuildHeaderMenus();
            UpdateHeaderMenuState();
        }
    }

    void BuildHeaderMenus()
    {
        if (HeaderMenus == null) return;
        HeaderMenus.Items.Clear();
        _saveRequiredItems.Clear();
        _noConsoleItems.Clear();
        _languageMenuItems.Clear();

        var file = TopMenu("menu.file");
        AddAction(file, "menu.file.open_directory", OpenSaveDirectory, shortcut: "Ctrl+O");
        AddAction(file, "menu.file.load_file", LoadSaveFile, shortcut: "Ctrl+L");
        file.Items.Add(new Separator());
        AddAction(file, "menu.file.save", SaveFromMenu, shortcut: "Ctrl+S", needsSave: true);
        AddAction(file, "menu.file.save_as", SaveAsCopy, shortcut: "Ctrl+Shift+S", needsSave: true);
        file.Items.Add(new Separator());
        AddAction(file, "menu.file.exit", CloseFromMenu, shortcut: "Alt+F4");
        HeaderMenus.Items.Add(file);

        var edit = TopMenu("menu.edit");
        AddAction(edit, "menu.edit.reload", ReloadCurrent, shortcut: "F5", needsSave: true);
        AddAction(edit, "menu.edit.restore_backup_all", RestoreLatestBackup, needsSave: true, noConsole: true);
        AddAction(edit, "menu.edit.restore_backup_single", RestoreSingleBackup, needsSave: true, noConsole: true);
        edit.Items.Add(new Separator());
        var watching = SubMenu(UiStrings.Get("menu.edit.save_watching"));
        AddWatchMode(watching, "Ask before reloading", "prompt");
        AddWatchMode(watching, "Reload automatically", "auto");
        AddWatchMode(watching, "Ignore changes", "ignore");
        edit.Items.Add(watching);
        HeaderMenus.Items.Add(edit);

        var tools = TopMenu("menu.tools");
        AddAction(tools, "menu.tools.export_json", ExportJson, needsSave: true);
        AddAction(tools, "menu.tools.import_json", ImportJson);
        tools.Items.Add(new Separator());
        AddAction(tools, "menu.tools.recharge_all_tech", () => RunBulkAction(_session.RechargeAllTechnology), needsSave: true);
        AddAction(tools, "menu.tools.refill_all_stacks", () => RunBulkAction(_session.RefillAllStacks), needsSave: true);
        AddAction(tools, "menu.tools.repair_all_slots", () => RunBulkAction(_session.RepairAllSlots), needsSave: true);
        AddAction(tools, "menu.tools.repair_all_tech", () => RunBulkAction(_session.RepairAllTechnology), needsSave: true);
        HeaderMenus.Items.Add(tools);

        var language = TopMenu("menu.language");
        foreach (string tag in LocalisationService.SupportedLanguages.Values)
        {
            var item = SubMenu(tag);
            item.IsCheckable = true;
            item.IsChecked = string.Equals(tag, _languageTag, StringComparison.OrdinalIgnoreCase);
            item.Tag = tag;
            item.Click += (_, _) => SelectLanguage(tag);
            language.Items.Add(item);
            _languageMenuItems.Add(item);
        }
        HeaderMenus.Items.Add(language);

        var help = TopMenu("menu.help");
        AddHelpLink(help, "menu.help.github", "https://github.com/frainkey1");
        AddAction(help, "menu.help.user_guide", ShowUserGuide);
        help.Items.Add(new Separator());
        AddHelpLink(help, "menu.help.sponsor", "https://www.paypal.com/donate/?hosted_button_id=RWT3F8XL7TTPE");
        help.Items.Add(new Separator());
        AddHelpLink(help, "menu.help.check_updates", "https://github.com/frainkey1/NMSFE");
        AddHelpLink(help, "menu.help.release_notes", "https://github.com/frainkey1/NMSFE/releases");
        help.Items.Add(new Separator());
        AddAction(help, "menu.help.about", ShowAbout);
        HeaderMenus.Items.Add(help);

        foreach (MenuItem top in HeaderMenus.Items.OfType<MenuItem>())
            StyleMenuItem(top, topLevel: true);
    }

    MenuItem TopMenu(string key) => SubMenu(UiStrings.Get(key));

    MenuItem SubMenu(string label)
    {
        var item = new MenuItem { Header = label.Replace("&", string.Empty, StringComparison.Ordinal), Padding = new Thickness(8, 5, 8, 5) };
        item.SetResourceReference(Control.ForegroundProperty, "Text");
        item.SetResourceReference(Control.BackgroundProperty, "Panel");
        return item;
    }

    void StyleMenuItem(MenuItem item, bool topLevel = false)
    {
        item.Padding = topLevel ? new Thickness(12, 6, 12, 6) : new Thickness(12, 7, 12, 7);
        item.Margin = topLevel ? new Thickness(2, 0, 2, 0) : new Thickness(0);
        item.MinWidth = topLevel ? 62 : 210;
        item.HorizontalContentAlignment = topLevel ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        item.VerticalContentAlignment = VerticalAlignment.Center;
        item.BorderThickness = new Thickness(0);
        item.BorderBrush = Brushes.Transparent;
        item.Template = HeaderMenuItemTemplate();
        if (topLevel) item.Background = Brushes.Transparent;
        item.SetResourceReference(Control.ForegroundProperty, topLevel ? "OnAccent" : "Text");
        if (!topLevel) item.SetResourceReference(Control.BackgroundProperty, "Panel");
        foreach (MenuItem child in item.Items.OfType<MenuItem>())
            StyleMenuItem(child);
    }

    ControlTemplate HeaderMenuItemTemplate() => _headerMenuItemTemplate ??= (ControlTemplate)XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         TargetType="{x:Type MenuItem}">
          <Grid SnapsToDevicePixels="True">
            <Border x:Name="ItemSurface" Background="{TemplateBinding Background}"
                    BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}"
                    CornerRadius="7" Padding="{TemplateBinding Padding}">
              <Grid>
                <Grid.ColumnDefinitions>
                  <ColumnDefinition Width="18"/>
                  <ColumnDefinition Width="*"/>
                  <ColumnDefinition Width="Auto"/>
                  <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>
                <TextBlock x:Name="CheckMark" Grid.Column="0" Text="✓" Visibility="Collapsed"
                           Foreground="{DynamicResource Accent}" VerticalAlignment="Center"/>
                <ContentPresenter Grid.Column="1" ContentSource="Header" RecognizesAccessKey="False"
                                  HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                                  VerticalAlignment="{TemplateBinding VerticalContentAlignment}"/>
                <TextBlock Grid.Column="2" Text="{TemplateBinding InputGestureText}"
                           Foreground="{DynamicResource Secondary}" Margin="24,0,10,0"
                           VerticalAlignment="Center"/>
                <TextBlock x:Name="SubmenuArrow" Grid.Column="3" Text="›" Visibility="Collapsed"
                           Foreground="{DynamicResource Secondary}" Margin="4,0,2,0"
                           VerticalAlignment="Center"/>
              </Grid>
            </Border>
            <Popup x:Name="PART_Popup" Placement="Bottom" AllowsTransparency="True" Focusable="False"
                   IsOpen="{Binding IsSubmenuOpen, RelativeSource={RelativeSource TemplatedParent}}"
                   PopupAnimation="Fade">
              <Border Background="{DynamicResource Panel}" BorderBrush="{DynamicResource Edge}"
                      BorderThickness="1" CornerRadius="10" Padding="5" SnapsToDevicePixels="True">
                <StackPanel IsItemsHost="True" KeyboardNavigation.DirectionalNavigation="Cycle"/>
              </Border>
            </Popup>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="Role" Value="TopLevelHeader">
              <Setter TargetName="PART_Popup" Property="Placement" Value="Bottom"/>
            </Trigger>
            <Trigger Property="Role" Value="SubmenuHeader">
              <Setter TargetName="PART_Popup" Property="Placement" Value="Right"/>
              <Setter TargetName="SubmenuArrow" Property="Visibility" Value="Visible"/>
            </Trigger>
            <Trigger Property="IsHighlighted" Value="True">
              <Setter TargetName="ItemSurface" Property="Background" Value="{DynamicResource Selection}"/>
              <Setter TargetName="ItemSurface" Property="BorderBrush" Value="{DynamicResource Accent}"/>
              <Setter TargetName="ItemSurface" Property="BorderThickness" Value="1"/>
            </Trigger>
            <Trigger Property="IsChecked" Value="True">
              <Setter TargetName="CheckMark" Property="Visibility" Value="Visible"/>
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter Property="Foreground" Value="{DynamicResource Secondary}"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
        """);

    void ApplyHeaderMenuPalette()
    {
        HeaderMenus.Resources[SystemColors.MenuBrushKey] = FindResource("Panel");
        HeaderMenus.Resources[SystemColors.MenuTextBrushKey] = FindResource("Text");
        HeaderMenus.Resources[SystemColors.MenuHighlightBrushKey] = FindResource("Selection");
        HeaderMenus.Resources[SystemColors.HighlightTextBrushKey] = FindResource("Text");
        HeaderMenus.Resources[SystemColors.ActiveBorderBrushKey] = FindResource("Edge");

        var separatorStyle = new Style(typeof(Separator));
        separatorStyle.Setters.Add(new Setter(Control.BackgroundProperty, FindResource("Edge")));
        separatorStyle.Setters.Add(new Setter(FrameworkElement.HeightProperty, 1d));
        separatorStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(10, 4, 10, 4)));
        HeaderMenus.Resources[typeof(Separator)] = separatorStyle;
    }

    void AddAction(MenuItem parent, string labelKey, Action action, string? shortcut = null,
        bool needsSave = false, bool noConsole = false)
    {
        var item = SubMenu(UiStrings.Get(labelKey));
        if (shortcut != null) item.InputGestureText = shortcut;
        item.Click += (_, _) => action();
        parent.Items.Add(item);
        if (needsSave) _saveRequiredItems.Add(item);
        if (noConsole) _noConsoleItems.Add(item);
    }

    void AddHelpLink(MenuItem parent, string labelKey, string url)
    {
        var item = SubMenu(UiStrings.Get(labelKey));
        item.ToolTip = url;
        item.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { PageError(ex); }
        };
        parent.Items.Add(item);
    }

    void ShowUserGuide()
    {
        var content = new StackPanel { Margin = new Thickness(18) };
        void AddSection(string title, string body)
        {
            content.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("Heading"), Margin = new Thickness(0, 8, 0, 3) });
            content.Children.Add(new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        }
        AddSection("Open a save", "Choose a save slot or use Open to select a supported save file, then Load it. The editor works on the loaded copy until you choose Save.");
        AddSection("Find your data", "Use the left navigation to open a page. Inventory pages have Cargo and Technology tabs; select a slot to view or edit its details. Use the search and section tabs on pages that list records.");
        AddSection("Edit safely", "Changes stay in memory until you save. Save writes the edited save and keeps the editor’s backup and validation safeguards. If the game changes the file while it is open, reload before saving your edits.");
        AddSection("Use the tools", "The Tools menu includes JSON import and export and bulk actions for charging technology, refilling stacks, and repairing damaged slots or technology.");
        AddSection("Change the view", "Use Language to choose a supported interface language and Light / Dark to switch the theme.");
        AddSection("More help", "Visit the GitHub Page for the project, Check for Updates to see the repository, or open Release Notes for published changes. Use Donate to support development.");
        var guide = new Window
        {
            Owner = this,
            Title = UiStrings.Get("menu.help.user_guide"),
            Width = 720,
            Height = 600,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
        };
        guide.ShowDialog();
    }

    void AddWatchMode(MenuItem parent, string label, string mode)
    {
        var item = SubMenu(label);
        item.IsCheckable = true;
        item.IsChecked = mode == _watchMode;
        item.Click += (_, _) => SetExternalChangeMode(mode);
        parent.Items.Add(item);
    }

    void UpdateHeaderMenuState()
    {
        bool loaded = _session.SourcePath != null;
        foreach (var item in _saveRequiredItems) item.IsEnabled = loaded && !_busy;
        foreach (var item in _noConsoleItems) item.IsEnabled = loaded && _session.ConsolePlatform == null && !_busy;
        UpdateLanguageMenuState();
    }

    void UpdateLanguageMenuState()
    {
        foreach (var item in _languageMenuItems)
            item.IsChecked = item.Tag is string tag &&
                string.Equals(tag, _languageTag, StringComparison.OrdinalIgnoreCase);
    }

    void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && (Keyboard.Modifiers & ModifierKeys.Shift) != 0 && e.Key == Key.S)
        {
            SaveAsCopy();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.O)
        {
            OpenSaveDirectory();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.L)
        {
            LoadSaveFile();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S)
        {
            SaveFromMenu();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.F5)
        {
            ReloadCurrent();
            e.Handled = true;
        }
    }

    void OpenSaveDirectory()
    {
        if (_busy) return;
        var dialog = new OpenFolderDialog { Title = "Choose a No Man's Sky save directory", Multiselect = false };
        if (_session.SourcePath is { } currentPath && Directory.Exists(Path.GetDirectoryName(currentPath)))
            dialog.InitialDirectory = Path.GetDirectoryName(currentPath)!;
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            string folder = Path.GetFullPath(dialog.FolderName);
            var choices = Directory.EnumerateFiles(folder)
                .Where(path => Path.GetExtension(path).Equals(".hg", StringComparison.OrdinalIgnoreCase) ||
                               Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
                .Where(path => !Path.GetFileName(path).StartsWith("savedata00", StringComparison.OrdinalIgnoreCase))
                .Select(path => new SaveChoice(path, Label(path)))
                .OrderBy(choice => choice.Label, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
            FileSelector.ItemsSource = choices;
            FileSelector.SelectedItem = choices.FirstOrDefault(choice =>
                string.Equals(choice.Path, _session.SourcePath, StringComparison.OrdinalIgnoreCase)) ?? choices.FirstOrDefault();
            Status.Text = choices.Length == 0 ? UiStrings.Format("ui.dynamic.5b5086081674", folder) : UiStrings.Format("ui.dynamic.63f08fe0b49a", choices.Length);
        }
        catch (Exception ex) { Error(ex); }
    }

    void LoadSaveFile() => ShowSaveOpenMenu();
    void SaveFromMenu() => SaveCurrent(this, new RoutedEventArgs());
    void CloseFromMenu() => Close();

    void SaveAsCopy()
    {
        if (_busy || _session.SourcePath == null || !CommitAllEdits()) return;
        if (_session.ConsolePlatform != null)
        {
            Error(new NotSupportedException("Save As is not available for console saves in this build."));
            return;
        }
        var dialog = new SaveFileDialog
        {
            Title = "Save a verified copy",
            Filter = "NMS save or JSON|*.hg;*.json|NMS save|*.hg|JSON save|*.json",
            InitialDirectory = Path.GetDirectoryName(_session.SourcePath),
            FileName = Path.GetFileName(_session.SourcePath),
            AddExtension = true,
            OverwritePrompt = false
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            _session.SaveAs(dialog.FileName);
            SyncLoadedFileChoice();
            ConfigureExternalWatcher();
            UpdateHeaderMenuState();
            Status.Text = UiStrings.Format("editor.verified_copy_saved", Path.GetFileName(dialog.FileName));
        }
        catch (Exception ex) { Error(ex); }
    }

    void ReloadCurrent()
    {
        if (_busy || _session.SourcePath == null) return;
        if (_session.ConsolePlatform == SaveFileManager.Platform.XboxGamePass &&
            _session.XboxContainerDirectory is { } container && _session.XboxSlotIdentifier is { } slot)
            _ = LoadPath(container, SaveFileManager.Platform.XboxGamePass, slot);
        else
            _ = LoadPath(_session.SourcePath, _session.ConsolePlatform);
    }

    void ExportJson()
    {
        if (_busy || _session.SourcePath == null || !CommitAllEdits()) return;
        var dialog = new SaveFileDialog
        {
            Title = "Export save JSON",
            Filter = "JSON file|*.json",
            FileName = Path.GetFileNameWithoutExtension(_session.SourcePath) + ".json",
            OverwritePrompt = true,
            DefaultExt = ".json"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, _session.Data.ToFormattedString());
            Status.Text = UiStrings.Format("editor.exported_file", Path.GetFileName(dialog.FileName));
        }
        catch (Exception ex) { Error(ex); }
    }

    void ImportJson()
    {
        if (_busy) return;
        var dialog = new OpenFileDialog { Title = "Import save JSON", Filter = "JSON file|*.json|All files|*.*" };
        if (dialog.ShowDialog(this) == true) _ = LoadPath(dialog.FileName);
    }

    void RunBulkAction(Func<int> action)
    {
        if (_busy || _session.SourcePath == null || !CommitAllEdits()) return;
        try
        {
            int count = action();
            RenderSelectedPage();
            Status.Text = count == 0 ? UiStrings.Get("ui.dynamic.d7a74d440a31") :
                UiStrings.Format("ui.dynamic.0639277cad2f", count);
        }
        catch (Exception ex) { Error(ex); }
    }

    void SelectLanguage(string tag)
    {
        if (_busy || !CommitAllEdits())
        {
            UpdateLanguageMenuState();
            return;
        }
        if (!LocalisationService.SupportedLanguages.Values.Contains(tag, StringComparer.OrdinalIgnoreCase))
        {
            UpdateLanguageMenuState();
            return;
        }
        _languageTag = tag;
        ApplyLanguagePreference();
        ClearPlayerPageState();
        ClearExosuitPageState();
        ClearDomainPageState();
        ClearUtilityPageState();
        BuildNavigationV2();
        ToolType.ItemsSource = MultitoolLogic.GetToolTypeItems();
        if (_activeTool >= 0)
        {
            if (_selectedPage == MainPageCatalog.MultiTools)
            {
                _session.RefreshTools();
                RefreshSelector();
            }
            ShowTool(_activeTool);
        }
        try { File.WriteAllText(LanguageSettingsPath, tag); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Status.Text = UiStrings.Format("ui.dynamic.c97d3f9f2030", ex.Message); }
        RenderSelectedPage();
        Status.Text = UiStrings.Format("status.language_set", tag);
        UiLocalizer.RefreshAll();
        if (_selectedSlot is null) SlotName.Text = UiStrings.Get("inventory.select_slot");
    }

    void ShowAbout()
    {
        string message = string.Join("\n",
            UiStrings.Get("about.product"),
            "",
            UiStrings.Get("about.derived_engine"),
            UiStrings.Get("about.github") + "https://github.com/frainkey1/NMSFE",
            UiStrings.Get("about.license") + "https://www.gnu.org/licenses/agpl-3.0.html");
        MessageBox.Show(this, message, UiStrings.Get("menu.help.about"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    void SetExternalChangeMode(string mode)
    {
        if (mode is not ("prompt" or "auto" or "ignore")) return;
        _watchMode = mode;
        try { File.WriteAllText(WatchSettingsPath, mode); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Status.Text = UiStrings.Format("editor.watch_mode_changed", ex.Message); }
        BuildHeaderMenus();
        UpdateHeaderMenuState();
        Status.Text = mode switch
        {
            "auto" => UiStrings.Get("ui.dynamic.129bd4c4fa5f"),
            "ignore" => UiStrings.Get("ui.dynamic.1d37ddb77f3a"),
            _ => UiStrings.Get("ui.dynamic.7d1230db8049")
        };
    }

    void ConfigureExternalWatcher()
    {
        StopExternalWatcher();
        if (_session.SourcePath is not { } path || _session.ConsolePlatform != null || !File.Exists(path)) return;
        string directory = Path.GetDirectoryName(path)!;
        string fileName = Path.GetFileName(path);
        try
        {
            _observedSourceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            _saveWatcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };
            _saveWatcher.Changed += OnSaveWatcherEvent;
            _saveWatcher.Created += OnSaveWatcherEvent;
            _saveWatcher.Deleted += OnSaveWatcherEvent;
            _saveWatcher.Renamed += OnSaveWatcherEvent;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            StopExternalWatcher();
            Status.Text = UiStrings.Get("editor.watch_unavailable") + " " + ex.Message;
        }
    }

    void StopExternalWatcher()
    {
        if (_saveWatcher != null)
        {
            _saveWatcher.EnableRaisingEvents = false;
            _saveWatcher.Changed -= OnSaveWatcherEvent;
            _saveWatcher.Created -= OnSaveWatcherEvent;
            _saveWatcher.Deleted -= OnSaveWatcherEvent;
            _saveWatcher.Renamed -= OnSaveWatcherEvent;
            _saveWatcher.Dispose();
            _saveWatcher = null;
        }
        _saveWatchTimer?.Stop();
        _observedSourceHash = null;
    }

    void OnSaveWatcherEvent(object sender, FileSystemEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_busy || _saveWatchTimer == null) return;
            _saveWatchTimer.Stop();
            _saveWatchTimer.Start();
        });
    }

    async Task EvaluateExternalSaveChange()
    {
        if (_busy || _session.SourcePath is not { } path) return;
        if (!File.Exists(path))
        {
            Status.Text = UiStrings.LocalizeText("The active save file was removed outside NMSFE. Do not save until you reload or choose another file.");
            return;
        }
        string current;
        try { current = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Status.Text = UiStrings.Get("editor.watch_check_failed") + " " + ex.Message; return; }
        if (string.Equals(current, _observedSourceHash, StringComparison.Ordinal)) return;
        _observedSourceHash = current;
        if (_watchMode == "ignore")
        {
            Status.Text = UiStrings.LocalizeText("An external save change was ignored. Reload before saving to preserve the newer file.");
            return;
        }
        if (_watchMode == "auto")
        {
            if (HasPendingChanges())
            {
                Status.Text = UiStrings.LocalizeText("The save changed on disk, but this session has edits. Save is blocked until you reload.");
                return;
            }
            await LoadPath(path, discardConfirmed: true);
            return;
        }
        bool hasEdits = HasPendingChanges();
        string message = hasEdits
            ? "The save changed outside NMSFE. Reload and discard your in-memory edits?"
            : "The save changed outside NMSFE. Reload it now?";
        if (MessageBox.Show(this, UiStrings.LocalizeText(message), UiStrings.LocalizeText("Save changed"), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            await LoadPath(path, discardConfirmed: true);
        else
            Status.Text = UiStrings.LocalizeText("External save change detected. Reload before saving to preserve the newer file.");
    }

    async void RestoreLatestBackup()
    {
        if (!CanRestoreBackups()) return;
        string? transaction = GetBackupTransactions().FirstOrDefault();
        if (transaction == null)
        {
            Status.Text = UiStrings.LocalizeText("No NMSFE backup transaction was found for this save.");
            return;
        }
        string[] files;
        try { files = SafeBackupFiles(transaction); }
        catch (Exception ex) { Error(ex); return; }
        if (files.Length == 0) { Status.Text = UiStrings.LocalizeText("The latest backup transaction is empty."); return; }
        string timestamp = Path.GetFileName(transaction);
        if (MessageBox.Show(this, UiStrings.LocalizeText($"Restore all {files.Length} backed-up file(s) from {timestamp}? Current files will be kept as rollback copies."),
            UiStrings.LocalizeText("Restore all backup files"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        if (!ConfirmDiscard()) return;
        try
        {
            RestoreBackupFiles(transaction, files);
            await ReloadAfterRestore();
            Status.Text = UiStrings.Format("editor.backup_files_restored", files.Length, timestamp);
        }
        catch (Exception ex) { Error(ex); }
    }

    async void RestoreSingleBackup()
    {
        if (!CanRestoreBackups()) return;
        var choices = GetBackupTransactions()
            .SelectMany(transaction => SafeBackupFiles(transaction).Select(path =>
                new BackupChoice(transaction, path, $"{Path.GetFileName(transaction)} · {Path.GetFileName(path)}")))
            .ToArray();
        if (choices.Length == 0) { Status.Text = UiStrings.LocalizeText("No NMSFE backup files were found for this save."); return; }

        var selected = ChooseBackup(choices);
        if (selected == null) return;
        if (MessageBox.Show(this, UiStrings.LocalizeText($"Restore {Path.GetFileName(selected.FilePath)} from {Path.GetFileName(selected.TransactionDirectory)}?"),
            UiStrings.LocalizeText("Restore one backup file"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        if (!ConfirmDiscard()) return;
        try
        {
            RestoreBackupFiles(selected.TransactionDirectory, [selected.FilePath]);
            await ReloadAfterRestore();
            Status.Text = UiStrings.Format("editor.backup_file_restored", Path.GetFileName(selected.FilePath));
        }
        catch (Exception ex) { Error(ex); }
    }

    bool CanRestoreBackups()
    {
        if (_busy || _session.SourcePath == null) return false;
        if (_session.ConsolePlatform != null)
        {
            Error(new NotSupportedException("Backup restore is not available for console saves in this build."));
            return false;
        }
        return true;
    }

    string[] GetBackupTransactions()
    {
        if (_session.SourcePath == null) return [];
        string root = Path.Combine(Path.GetDirectoryName(_session.SourcePath)!, ".nmse-next-backups");
        if (!Directory.Exists(root)) return [];
        return Directory.GetDirectories(root)
            .Where(path => !IsReparsePoint(path))
            .OrderByDescending(Directory.GetLastWriteTimeUtc)
            .ToArray();
    }

    static string[] SafeBackupFiles(string directory)
    {
        try
        {
            return Directory.GetFiles(directory)
                .Where(path => !IsReparsePoint(path) && Path.GetFileName(path) == Path.GetFileName(Path.GetFullPath(path)))
                .ToArray();
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    static bool IsReparsePoint(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch { return true; }
    }

    BackupChoice? ChooseBackup(BackupChoice[] choices)
    {
        var combo = new ComboBox { ItemsSource = choices, DisplayMemberPath = nameof(BackupChoice.Label), SelectedIndex = 0, MinWidth = 440 };
        var body = new StackPanel { Margin = new Thickness(16) };
        body.Children.Add(new TextBlock { Text = "Choose one backup file to restore:", Margin = new Thickness(3, 3, 3, 8) });
        body.Children.Add(combo);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var dialog = new Window
        {
            Owner = this, Title = "Restore one backup file", Content = body,
            SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        dialog.SetResourceReference(BackgroundProperty, "Panel");
        dialog.SetResourceReference(ForegroundProperty, "Text");
        var restore = new Button { Content = UiStrings.Get("ui.backup.choose"), IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        restore.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(restore);
        buttons.Children.Add(cancel);
        body.Children.Add(buttons);
        return dialog.ShowDialog() == true ? combo.SelectedItem as BackupChoice : null;
    }

    void RestoreBackupFiles(string transaction, IEnumerable<string> backupFiles)
    {
        if (_session.SourcePath == null) throw new InvalidOperationException("Load a save before restoring a backup.");
        string root = Path.GetFullPath(Path.GetDirectoryName(_session.SourcePath)!);
        string backupRoot = Path.GetFullPath(Path.Combine(root, ".nmse-next-backups"));
        string transactionPath = Path.GetFullPath(transaction);
        if (!transactionPath.StartsWith(backupRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            IsReparsePoint(transactionPath))
            throw new IOException("The selected backup transaction is outside this save's backup folder.");
        var sources = backupFiles.Select(Path.GetFullPath).ToArray();
        if (sources.Length == 0) throw new InvalidDataException("The selected backup contains no files.");
        foreach (string source in sources)
            if (!source.StartsWith(transactionPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                IsReparsePoint(source) || Path.GetFileName(source) != Path.GetFileName(Path.GetFullPath(source)))
                throw new IOException("A backup file has an invalid path.");

        string staging = Path.Combine(root, ".nmse-next-restore-" + Guid.NewGuid().ToString("N"));
        string backupRootPath = Path.Combine(root, ".nmse-next-backups");
        Directory.CreateDirectory(backupRootPath);
        if (IsReparsePoint(backupRootPath)) throw new IOException("The save backup directory cannot be a link or junction.");
        string rollbackDirectory = Path.Combine(backupRootPath,
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-restore-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(staging);
        Directory.CreateDirectory(rollbackDirectory);
        string newFiles = Path.Combine(staging, "new");
        string rollbackFiles = Path.Combine(staging, "rollback");
        Directory.CreateDirectory(newFiles);
        Directory.CreateDirectory(rollbackFiles);
        var moved = new List<(string Target, string? Rollback)>();
        try
        {
            foreach (string source in sources)
            {
                string target = Path.Combine(root, Path.GetFileName(source));
                string staged = Path.Combine(newFiles, Path.GetFileName(source));
                string? rollback = File.Exists(target) ? Path.Combine(rollbackFiles, Path.GetFileName(source)) : null;
                File.Copy(source, staged, false);
                if (rollback != null)
                {
                    File.Copy(target, rollback, false);
                    File.Copy(target, Path.Combine(rollbackDirectory, Path.GetFileName(source)), false);
                }
            }
            string activeName = Path.GetFileName(_session.SourcePath);
            string stagedActive = Path.Combine(newFiles, activeName);
            if (sources.Any(source => string.Equals(Path.GetFileName(source), activeName, StringComparison.OrdinalIgnoreCase)) &&
                File.Exists(stagedActive))
                _ = SaveFileManager.LoadSaveFile(stagedActive);

            foreach (string source in sources)
            {
                string target = Path.Combine(root, Path.GetFileName(source));
                string staged = Path.Combine(newFiles, Path.GetFileName(source));
                string rollback = Path.Combine(rollbackFiles, Path.GetFileName(source));
                File.Move(staged, target, true);
                moved.Add((target, File.Exists(rollback) ? rollback : null));
            }
        }
        catch
        {
            foreach (var (target, rollback) in moved.AsEnumerable().Reverse())
            {
                if (rollback != null && File.Exists(rollback)) File.Copy(rollback, target, true);
                else if (File.Exists(target)) File.Delete(target);
            }
            throw;
        }
        finally
        {
            if (Directory.Exists(staging) &&
                Path.GetFullPath(staging).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(staging).StartsWith(".nmse-next-restore-", StringComparison.Ordinal))
                Directory.Delete(staging, true);
        }
    }

    async Task ReloadAfterRestore()
    {
        if (_session.SourcePath is not { } path) return;
        await LoadPath(path, discardConfirmed: true);
    }
}
