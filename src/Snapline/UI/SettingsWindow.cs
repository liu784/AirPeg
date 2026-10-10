using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Snapline.Core;
using Snapline.Interop;

namespace Snapline.UI;

/// <summary>Everything you can change, on one page. Changes apply as you make them.</summary>
public sealed class SettingsWindow : Window
{
    private static SettingsWindow? _open;
    private static int? _shortcutError, _previewShortcutError;
    private TextBox _previewHotKey = null!;
    private TextBlock _previewShortcutStatus = null!;
    public static bool IsShortcutEditing => _open is { } w && (w._hotKey.IsKeyboardFocusWithin || w._previewHotKey.IsKeyboardFocusWithin);
    private TextBlock _shortcutStatus = null!;
    private readonly Action _apply;
    private readonly Action? _previewDelays;
    private readonly Action? _behaviorChanged;
    private CheckBox _lockSetting = null!;
    private TextBox _hotKey = null!;
    private ListBox _folders = null!;
    private TextBlock _inbox = null!;
    private TextBox _offset = null!;
    private bool _building;

    public static void Open(Action apply, Action? previewDelays = null, Action? behaviorChanged = null)
    {
        if (_open is not null) { _open.Activate(); return; }
        _open = new SettingsWindow(apply, previewDelays, behaviorChanged);
        _open.Closed += (_, _) => _open = null;
        _open.Show();
    }

    public static string ShortcutStatus(int error, string? shortcut = null) => error switch
    {
        0 => Strings.ShortcutReady,
        1409 => string.Format(Strings.ShortcutOccupied, shortcut ?? Settings.Current.HotKey),
        87 => Strings.ShortcutReserved,
        _ => string.Format(Strings.ShortcutFailed, error),
    };

    public static void SetShortcutError(int error)
    {
        _shortcutError = error;
        _open?.RefreshShortcutStatus();
    }

    public static void SetPreviewShortcutError(int? error)
    {
        _previewShortcutError = error;
        _open?.RefreshPreviewShortcutStatus();
    }

    private void RefreshPreviewShortcutStatus()
    {
        if (_previewShortcutStatus is null) return;
        bool show = Settings.Current.QuickPreviewEnabled && Settings.Current.ShortcutConflictAlerts && _previewShortcutError is not null;
        bool single = HotKey.Parse(Settings.Current.PreviewHotKey, out var mods, out _) && mods == 0;
        _previewShortcutStatus.Text = show ? single && _previewShortcutError == 0
            ? Strings.PreviewSingleKeyReady : ShortcutStatus(_previewShortcutError!.Value, Settings.Current.PreviewHotKey) : "";
        _previewShortcutStatus.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        _previewShortcutStatus.Opacity = _previewShortcutError == 0 ? 0.65 : 1;
    }

    private void RefreshShortcutStatus()
    {
        bool show = Settings.Current.ShortcutConflictAlerts && _shortcutError is not null;
        _shortcutStatus.Text = show ? ShortcutStatus(_shortcutError!.Value) : "";
        _shortcutStatus.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        _shortcutStatus.Opacity = _shortcutError == 0 ? 0.65 : 1;
        RefreshPreviewShortcutStatus();
    }

    public static void RefreshLockState()
    {
        if (_open is null) return;
        _open._building = true;
        try { _open._lockSetting.IsChecked = Settings.Current.LockHangerEnabled; }
        finally { _open._building = false; }
    }

    /// <summary>After a language change the page is rebuilt in place.</summary>
    public static void Rebuild()
    {
        if (_open is null) return;
        var apply = _open._apply;
        var preview = _open._previewDelays;
        var behavior = _open._behaviorChanged;
        var left = _open.Left; var top = _open.Top;
        _open.Close();
        Open(apply, preview, behavior);
        if (_open is not null) { _open.Left = left; _open.Top = top; }
    }

    /// <summary>The settings page as a plain element, for the film.</summary>
    public static FrameworkElement ForDemo(Action apply)
    {
        var w = new SettingsWindow(apply, demo: true);
        var content = (FrameworkElement)w.Content;
        w.Content = null;
        w.Close();
        if (content is ScrollViewer sv) { sv.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled; sv.MaxHeight = double.PositiveInfinity; }
        var border = new Border { Width = 500, Background = w.Background, Child = content };
        System.Windows.Documents.TextElement.SetForeground(border, w.Foreground);
        return border;
    }

    private SettingsWindow(Action apply, Action? previewDelays = null, Action? behaviorChanged = null, bool demo = false)
    {
        _apply = apply;
        _previewDelays = previewDelays;
        _behaviorChanged = behaviorChanged;
        Title = $"{Strings.AppName} · {Strings.SettingsTitle}";
        Width = demo ? 500 : 760; Height = Math.Min(680, SystemParameters.WorkArea.Height - 48);
        SizeToContent = demo ? SizeToContent.Height : SizeToContent.Manual;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        Icon = Brand.Icon;
        FlowDirection = Strings.Flow;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 13;
        UseLayoutRounding = true;
        Paint();
        Theme.Changed += Paint;
        Closed += (_, _) => Theme.Changed -= Paint;
        var content = Build(demo);
        if (demo) Content = content; else GlassWindow.Attach(this, content, textSafe: true);
    }

    private void Paint()
    {
        Background = (Brush)Application.Current.Resources["MenuBg"];
        Foreground = (Brush)Application.Current.Resources["MenuFg"];
    }

    private UIElement Build(bool demo)
    {
        _building = true;
        var s = Settings.Current;
        var stack = new StackPanel { Margin = new Thickness(24, 16, 24, 18) };
        var header = Brand.Lockup(26, Theme.AppsDark);
        header.Margin = new Thickness(0, 0, 0, 6);
        stack.Children.Add(header);

        // The line
        stack.Children.Add(Heading(Strings.SectionLine));
        stack.Children.Add(Check(Strings.RevealAtTopEdge, Strings.RevealAtTopEdgeTip, s.RevealAtTopEdge, v => s.RevealAtTopEdge = v));
        stack.Children.Add(Check(Strings.StayDownWhileUnused, Strings.StayDownWhileUnusedTip, s.StayDownWhileUnused, v => s.StayDownWhileUnused = v));
        stack.Children.Add(Check(Strings.TakeDownAfterDrag, Strings.TakeDownAfterDragTip, s.TakeDownAfterDrag, v => s.TakeDownAfterDrag = v));

        _lockSetting = Check(Strings.LockHangerEnabled, Strings.LockTip, s.LockHangerEnabled,
            v => { s.LockHangerEnabled = v; s.LockExpanded = v; }, behavior: true);
        System.Windows.Automation.AutomationProperties.SetAutomationId(_lockSetting, "LockExpandedSetting");
        stack.Children.Add(_lockSetting);
        var fullScreen = Check(Strings.FullScreenHide, Strings.FullScreenHideTip, s.FullScreenHideEnabled,
            v => s.FullScreenHideEnabled = v, behavior: true);
        System.Windows.Automation.AutomationProperties.SetAutomationId(fullScreen, "FullScreenHideEnabled");
        stack.Children.Add(fullScreen);

        _hotKey = new TextBox { Text = s.HotKey, IsReadOnly = true, Width = 170, Padding = new Thickness(8, 4, 8, 4), ToolTip = Strings.ShortcutTip, HorizontalAlignment = HorizontalAlignment.Left };
        System.Windows.Automation.AutomationProperties.SetAutomationId(_hotKey, "LineShortcut");
        _hotKey.PreviewKeyDown += OnHotKeyPressed;
        stack.Children.Add(Row(Strings.Shortcut, _hotKey));
        _shortcutStatus = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 4) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(_shortcutStatus, "ShortcutStatus");
        RefreshShortcutStatus(); stack.Children.Add(_shortcutStatus);
        var conflictAlerts = Check(Strings.ShortcutAlerts, Strings.ShortcutAlertsTip, s.ShortcutConflictAlerts,
            v => { s.ShortcutConflictAlerts = v; RefreshShortcutStatus(); }, behavior: true);
        System.Windows.Automation.AutomationProperties.SetAutomationId(conflictAlerts, "ShortcutConflictAlerts");
        stack.Children.Add(conflictAlerts);

        _offset = new TextBox { Text = ((int)s.LineOffset).ToString(), Width = 70, Padding = new Thickness(8, 4, 8, 4), ToolTip = Strings.LineDistanceTip, HorizontalAlignment = HorizontalAlignment.Left };
        _offset.LostFocus += (_, _) => { if (double.TryParse(_offset.Text, out var v)) { s.LineOffset = Math.Max(0, v); Changed(); } };
        _offset.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Keyboard.ClearFocus(); if (double.TryParse(_offset.Text, out var v)) { s.LineOffset = Math.Max(0, v); Changed(); } } };
        stack.Children.Add(Row(Strings.LineDistance, _offset, Strings.LineDistanceTip));

        // Independent panel speeds. Saving these never rebuilds the line or its cards.
        stack.Children.Add(Heading(Strings.SectionAnimation));
        stack.Children.Add(SpeedRow(Strings.ExpandSpeed, "ExpandSpeed", s.ExpandSpeed, v => s.ExpandSpeed = v, out var expandSpeed));
        stack.Children.Add(SpeedRow(Strings.HideSpeed, "HideSpeed", s.HideSpeed, v => s.HideSpeed = v, out var hideSpeed));
        var resetSpeeds = new Button
        {
            Content = Strings.ResetSpeeds, HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 6, 0, 2),
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(resetSpeeds, "ResetAnimationSpeeds");
        resetSpeeds.Click += (_, _) =>
        {
            _building = true;
            s.ExpandSpeed = s.HideSpeed = 1;
            expandSpeed.Value = hideSpeed.Value = 1;
            _building = false;
            s.Save();
        };
        stack.Children.Add(resetSpeeds);

        stack.Children.Add(Heading(Strings.SectionDelays));
        stack.Children.Add(DelayRow(Strings.RevealDelay, "RevealDelay", s.RevealDelay, v => s.RevealDelay = v));
        stack.Children.Add(DelayRow(Strings.HideDelay, "HideDelay", s.HideDelay, v => s.HideDelay = v));
        var preview = new Button { Content = Strings.PreviewDelays, HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 6, 0, 2) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(preview, "PreviewDelays");
        preview.Click += (_, _) => _previewDelays?.Invoke();
        stack.Children.Add(preview);

        // Look
        stack.Children.Add(Heading(Strings.SectionLook));

        var glass = new CheckBox { Content = Strings.LiquidGlass, IsChecked = s.LiquidGlassEnabled, Margin = new Thickness(0,4,0,10) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(glass, "LiquidGlassEnabled");
        System.Windows.Automation.AutomationProperties.SetName(glass, Strings.LiquidGlass);
        var density = new Slider { Minimum=0, Maximum=100, Value=s.GlassIntensity, SmallChange=1, LargeChange=10, Width=154, IsEnabled=s.LiquidGlassEnabled };
        System.Windows.Automation.AutomationProperties.SetAutomationId(density,"GlassIntensity");
        System.Windows.Automation.AutomationProperties.SetName(density,Strings.GlassIntensity);
        var densityValue = new TextBlock { Width=40, VerticalAlignment=VerticalAlignment.Center, TextAlignment=TextAlignment.Right, Text=s.GlassIntensity.ToString("0") };
        var densityPanel = new StackPanel { Orientation=Orientation.Horizontal }; densityPanel.Children.Add(density); densityPanel.Children.Add(densityValue);
        void AppearanceChanged() { if (_building) return; s.Save(); Theme.ApplySetting(); }
        glass.Checked += (_,_) => { s.LiquidGlassEnabled=true; density.IsEnabled=true; AppearanceChanged(); };
        glass.Unchecked += (_,_) => { s.LiquidGlassEnabled=false; density.IsEnabled=false; AppearanceChanged(); };
        density.ValueChanged += (_,_) => { densityValue.Text=density.Value.ToString("0"); if(_building)return; s.GlassIntensity=density.Value; AppearanceChanged(); };
        stack.Children.Add(glass);
        stack.Children.Add(Row(Strings.GlassIntensity, densityPanel));
        stack.Children.Add(new TextBlock { Text=Strings.GlassDescription, TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,8,0,14), FontSize=12 });
        var appearance = Choice(new[] { ("auto", Strings.Auto), ("light", Strings.Light), ("dark", Strings.Dark) }, s.Appearance, v => s.Appearance = v, appearanceOnly: true);
        System.Windows.Automation.AutomationProperties.SetAutomationId(appearance, "AppearanceMode");
        stack.Children.Add(Row(Strings.Appearance, appearance));
        var resetAppearance = new Button { Content=Strings.ResetAppearance, Margin=new Thickness(0,12,0,8), HorizontalAlignment=HorizontalAlignment.Left };
        System.Windows.Automation.AutomationProperties.SetAutomationId(resetAppearance,"ResetAppearance");
        resetAppearance.Click += (_,_) => {
            _building=true;
            try { s.LiquidGlassEnabled=true; s.GlassIntensity=50; s.Appearance="auto"; glass.IsChecked=true; density.Value=50; density.IsEnabled=true; appearance.SelectedIndex=0; }
            finally { _building=false; }
            AppearanceChanged();
        };
        stack.Children.Add(resetAppearance);

        var language = Choice(new[] { ("zh-CN", "简体中文"), ("en", "English") }, Strings.Language, v => { s.Language = v; Dispatcher.BeginInvoke(Rebuild); });
        stack.Children.Add(Row(Strings.LanguageLabel, language));

        stack.Children.Add(Heading(Strings.HistoryTitle));
        var history = Check(Strings.HistoryEnabled, Strings.HistoryPolicy, s.HistoryEnabled, v => s.HistoryEnabled = v, behavior: true);
        System.Windows.Automation.AutomationProperties.SetAutomationId(history, "HistoryEnabled");
        stack.Children.Add(history);
        stack.Children.Add(new TextBlock { Text = Strings.HistoryPolicy, TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 11 });
        stack.Children.Add(Heading(Strings.QuickPreview));
        var quick = Check(Strings.QuickPreviewEnabled, Strings.QuickPreviewTip, s.QuickPreviewEnabled, v => { s.QuickPreviewEnabled = v; RefreshPreviewShortcutStatus(); }, behavior: true);
        System.Windows.Automation.AutomationProperties.SetAutomationId(quick, "QuickPreviewEnabled");
        stack.Children.Add(quick);
        stack.Children.Add(new TextBlock { Text = Strings.QuickPreviewTip, TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 11 });

        _previewHotKey = new TextBox { Text = s.PreviewHotKey, IsReadOnly = true, Width = 170,
            Padding = new Thickness(8, 4, 8, 4), ToolTip = Strings.PreviewShortcutTip, HorizontalAlignment = HorizontalAlignment.Left };
        System.Windows.Automation.AutomationProperties.SetAutomationId(_previewHotKey, "PreviewShortcut");
        _previewHotKey.PreviewKeyDown += OnHotKeyPressed;
        stack.Children.Add(Row(Strings.PreviewShortcut, _previewHotKey));
        stack.Children.Add(new TextBlock { Text = Strings.PreviewShortcutTip, TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 11 });
        _previewShortcutStatus = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 4) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(_previewShortcutStatus, "PreviewShortcutStatus");
        RefreshPreviewShortcutStatus(); stack.Children.Add(_previewShortcutStatus);

        // Captures
        stack.Children.Add(Heading(Strings.SectionCaptures));
        stack.Children.Add(Check(Strings.CatchClipboard, Strings.CatchClipboardTip, s.CatchClipboard, v => s.CatchClipboard = v));

        var inboxRow = new DockPanel { Margin = new Thickness(0, 6, 0, 2) };
        var change = new Button { Content = Strings.Change, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(change, Dock.Right);
        change.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = Strings.OpenInboxFolder, InitialDirectory = Inbox.Folder };
            if (dialog.ShowDialog(this) != true) return;
            var guarded = new List<string> { Shell.ScreenshotsFolder(), Shell.Desktop(),
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) };
            guarded.AddRange(s.WatchFolders.Select(Environment.ExpandEnvironmentVariables));
            if (!Inbox.IsSafeFolder(dialog.FolderName, guarded))
            {
                GlassDialog.Show(this, Strings.InboxFolderRefused, Strings.AppName);
                return;
            }
            s.InboxFolder = dialog.FolderName; Changed(); _inbox.Text = Inbox.Folder;
        };
        inboxRow.Children.Add(change);
        _inbox = new TextBlock { Text = Inbox.Folder, TextTrimming = TextTrimming.CharacterEllipsis, Opacity = 0.7, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = Strings.IsRtl ? HorizontalAlignment.Right : HorizontalAlignment.Left };
        var inboxLabel = new StackPanel();
        inboxLabel.Children.Add(new TextBlock { Text = Strings.CaughtCapturesFolder });
        inboxLabel.Children.Add(_inbox);
        inboxRow.Children.Add(inboxLabel);
        stack.Children.Add(inboxRow);

        stack.Children.Add(new TextBlock { Text = Strings.WatchFoldersLabel, Margin = new Thickness(0, 12, 0, 4) });
        _folders = new ListBox { Height = 84, Margin = new Thickness(0, 0, 0, 6), FlowDirection = FlowDirection.LeftToRight };
        _folders.Items.Add(new ListBoxItem { Content = Shell.ScreenshotsFolder(), IsEnabled = false, Opacity = 0.6 });
        foreach (var f in s.WatchFolders) _folders.Items.Add(f);
        stack.Children.Add(_folders);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var add = new Button { Content = Strings.AddFolder, Padding = new Thickness(12, 4, 12, 4) };
        add.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = Strings.AddFolder };
            if (dialog.ShowDialog(this) == true && !s.WatchFolders.Contains(dialog.FolderName, StringComparer.OrdinalIgnoreCase))
            {
                s.WatchFolders.Add(dialog.FolderName);
                _folders.Items.Add(dialog.FolderName);
                Changed();
            }
        };
        var remove = new Button { Content = Strings.RemoveFolder, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0) };
        remove.Click += (_, _) =>
        {
            if (_folders.SelectedItem is string f)
            {
                s.WatchFolders.RemoveAll(x => string.Equals(x, f, StringComparison.OrdinalIgnoreCase));
                _folders.Items.Remove(f);
                Changed();
            }
        };
        buttons.Children.Add(add);
        buttons.Children.Add(remove);
        stack.Children.Add(buttons);

        // General
        stack.Children.Add(Heading(Strings.SectionGeneral));
        stack.Children.Add(Check(Strings.Sounds, null, s.SoundOn, v => { s.SoundOn = v; Sounds.Enabled = v; }));
        stack.Children.Add(Check(Strings.StartWithWindows, null, Shell.StartsWithWindows, v =>
        {
            try { Shell.StartsWithWindows = v; } catch (Exception e) { Log.Error($"Could not change the startup setting: {e.Message}"); }
        }));

        stack.Children.Add(new TextBlock
        {
            Text = string.Format(Strings.SettingsFooter, System.IO.Path.Combine(Settings.Folder, "settings.json")),
            Margin = new Thickness(0, 16, 0, 0), Opacity = 0.6, FontSize = 11, TextWrapping = TextWrapping.Wrap,
        });
        _building = false;
        if (demo) return new ScrollViewer { Content=stack, VerticalScrollBarVisibility=ScrollBarVisibility.Auto, MaxHeight=SystemParameters.WorkArea.Height-80 };
        return GroupedPage(stack);
    }

    private UIElement GroupedPage(StackPanel flat)
    {
        var groups = new List<(string name, StackPanel panel)>();
        StackPanel? current = null;
        foreach (var child in flat.Children.Cast<UIElement>().ToArray())
        {
            flat.Children.Remove(child);
            if (child is TextBlock { Tag: "section" } heading)
            {
                current=new StackPanel(); groups.Add((heading.Text,current));
                heading.FontSize=18; heading.Margin=new Thickness(0,0,0,16); current.Children.Add(heading);
            }
            else current?.Children.Add(child);
        }
        var root = new DockPanel { Margin=new Thickness(18,14,18,18) };
        var header = new DockPanel { Margin=new Thickness(2,0,2,18) };
        var mark=Brand.Lockup(24, Theme.AppsDark);
        if(mark is StackPanel brandRow) foreach(var label in brandRow.Children.OfType<TextBlock>()) label.SetResourceReference(TextBlock.ForegroundProperty,"ChromeText");
        mark.HorizontalAlignment=HorizontalAlignment.Right; DockPanel.SetDock(mark,Dock.Right); header.Children.Add(mark);
        header.Children.Add(new TextBlock { Text=Strings.SettingsTitle, FontSize=24, FontWeight=FontWeights.SemiBold });
        DockPanel.SetDock(header,Dock.Top); root.Children.Add(header);
        var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(146) }); body.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) }); root.Children.Add(body);
        var nav = new ListBox { BorderThickness=new Thickness(0), Background=Brushes.Transparent, Margin=new Thickness(0,0,12,0), Padding=new Thickness(0), VerticalAlignment=VerticalAlignment.Top };
        System.Windows.Automation.AutomationProperties.SetAutomationId(nav,"SettingsNavigation");
        body.Children.Add(nav);
        var pages = new Grid(); Grid.SetColumn(pages,1); body.Children.Add(pages);
        var ordered=groups.OrderBy(g=>g.name==Strings.SectionLook?0:1).ToList();
        foreach (var group in ordered)
        {
            var scroll=new ScrollViewer { Content=group.panel, VerticalScrollBarVisibility=ScrollBarVisibility.Auto, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled, Padding=new Thickness(18,18,14,18) };
            var surface=new Border { CornerRadius=new CornerRadius(16), BorderThickness=new Thickness(1), Child=scroll, Visibility=Visibility.Collapsed };
            surface.SetResourceReference(Border.BackgroundProperty,"ChromeReadingPanel"); surface.SetResourceReference(Border.BorderBrushProperty,"SettingsContentEdge"); pages.Children.Add(surface);
            nav.Items.Add(new ListBoxItem { Content=new TextBlock { Text=group.name,TextWrapping=TextWrapping.Wrap },Tag=surface,Margin=new Thickness(0,0,0,4),Padding=new Thickness(10,10,8,10) });
        }
        nav.SelectionChanged += (_,_) => { foreach(Border page in pages.Children) page.Visibility=Visibility.Collapsed; if(nav.SelectedItem is ListBoxItem { Tag: Border selected }) selected.Visibility=Visibility.Visible; };
        nav.SelectedIndex=0;
        return root;
    }

    private TextBlock Heading(string text) => new()
    {
        Text = text, Tag = "section", FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 12, 0, 6),
    };

    private static DockPanel Row(string label, UIElement control, string? tip = null)
    {
        var row = new DockPanel { Margin = new Thickness(0, 6, 0, 2), LastChildFill = false };
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Width = 210, TextWrapping = TextWrapping.Wrap, ToolTip = tip };
        row.Children.Add(text);
        row.Children.Add(control);
        return row;
    }

    private DockPanel SpeedRow(string label, string id, double value, Action<double> set, out Slider slider)
    {
        var controls = new DockPanel { Width = 200 };
        var readout = new TextBlock { Width = 42, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(readout, Dock.Right);
        controls.Children.Add(readout);
        var speed = new Slider
        {
            Minimum = 0.5, Maximum = 2, TickFrequency = 0.1, SmallChange = 0.1, LargeChange = 0.1,
            IsSnapToTickEnabled = true, Value = value, VerticalAlignment = VerticalAlignment.Center,
            ToolTip = Strings.AnimationSpeedTip,
        };
        System.Windows.Automation.AutomationProperties.SetName(speed, label);
        System.Windows.Automation.AutomationProperties.SetAutomationId(speed, id);
        void UpdateReadout() => readout.Text = speed.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "×";
        speed.ValueChanged += (_, _) =>
        {
            UpdateReadout();
            if (_building) return;
            set(speed.Value);
            Settings.Current.Save();
        };
        UpdateReadout();
        controls.Children.Add(speed);
        slider = speed;
        return Row(label, controls, Strings.AnimationSpeedTip);
    }

    private DockPanel DelayRow(string label, string id, double value, Action<double> set)
    {
        var controls = new DockPanel { Width = 200 };
        var readout = new TextBlock { Width = 42, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(readout, Dock.Right); controls.Children.Add(readout);
        var delay = new Slider { Minimum = 0, Maximum = 3, TickFrequency = 0.1, SmallChange = 0.1, LargeChange = 0.1,
            IsSnapToTickEnabled = true, Value = value, VerticalAlignment = VerticalAlignment.Center, ToolTip = Strings.DelayTip };
        System.Windows.Automation.AutomationProperties.SetName(delay, label);
        System.Windows.Automation.AutomationProperties.SetAutomationId(delay, id);
        void Readout() => readout.Text = delay.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s";
        delay.ValueChanged += (_, _) => { Readout(); if (_building) return; set(delay.Value); Settings.Current.Save(); };
        Readout(); controls.Children.Add(delay);
        return Row(label, controls, Strings.DelayTip);
    }

    private CheckBox Check(string label, string? tip, bool value, Action<bool> set, bool behavior = false)
    {
        var box = new CheckBox { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, IsChecked = value, Margin = new Thickness(0, 4, 0, 4), ToolTip = tip };
        System.Windows.Automation.AutomationProperties.SetName(box, label);
        void Apply(bool on) { set(on); if (_building) return; if (behavior) { Settings.Current.Save(); _behaviorChanged?.Invoke(); } else Changed(); }
        box.Checked += (_, _) => Apply(true);
        box.Unchecked += (_, _) => Apply(false);
        return box;
    }

    private ComboBox Choice((string key, string label)[] options, string current, Action<string> set, bool appearanceOnly = false)
    {
        var box = new ComboBox { Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var (_, label) in options) box.Items.Add(label);
        int index = Array.FindIndex(options, o => o.key == current);
        box.SelectedIndex = index < 0 ? 0 : index;
        box.SelectionChanged += (_, _) => { if (box.SelectedIndex >= 0) { set(options[box.SelectedIndex].key); if (appearanceOnly) { if (!_building) { Settings.Current.Save(); Theme.ApplySetting(); } } else Changed(); } };
        return box;
    }

    private void OnHotKeyPressed(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
        if (sender == _previewHotKey)
        {
            if (e.IsRepeat || HotKey.FromKey(key, Keyboard.Modifiers) is not { } previewKey) return;
            Settings.Current.PreviewHotKey = previewKey;
            _previewHotKey.Text = previewKey;
            Settings.Current.Save();
            _behaviorChanged?.Invoke();
            return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        if (key == Key.Escape) return;
        var parts = new List<string>();
        var mods = Keyboard.Modifiers;
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        if (parts.Count == 0 && key is not (>= Key.F1 and <= Key.F24)) return; // a bare letter would steal typing everywhere
        parts.Add(key.ToString());
        var text = string.Join("+", parts);
        if (!HotKey.Parse(text, out _, out _)) return;
        Settings.Current.HotKey = text;
        _hotKey.Text = text;
        Changed();
    }

    private void Changed()
    {
        if (_building) return;
        Settings.Current.Save();
        _apply();
    }
}
