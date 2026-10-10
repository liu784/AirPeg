using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Snapline.Core;
using Snapline.Interop;

namespace Snapline.UI;

/// <summary>Read-only, bounded-resolution viewer. Never opens the editor or writes image files.</summary>
public sealed class PreviewWindow : Window
{
    private static PreviewWindow? _open;
    public static bool IsOpen => _open is not null;
    private readonly Image _image = new() { Stretch = Stretch.Uniform, Margin = new Thickness(12) };
    private readonly TextBlock _caption = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private int _loadGeneration;
    private bool _closed;
    public static void Open(string path)
    {
        if (!Settings.Current.QuickPreviewEnabled) return;
        if (_open is null) { _open = new PreviewWindow(); _open.Show(); }
        _open.Activate(); _ = _open.LoadImage(path);
    }
    public static void CloseCurrent() => _open?.Close();
    public static void Toggle(string? path)
    {
        if (IsOpen) CloseCurrent();
        else if (path is not null) Open(path);
    }
    public PreviewWindow()
    {
        Title = Strings.QuickPreview; Width = Math.Min(1000, SystemParameters.WorkArea.Width - 60);
        Height = Math.Min(740, SystemParameters.WorkArea.Height - 60); MinWidth = 320; MinHeight = 240;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Icon = Brand.Icon;
        Paint(); Theme.Changed += Paint; Strings.LanguageChanged += CloseForLanguage;
        var root = new DockPanel(); GlassWindow.Attach(this, root);
        var bar = new DockPanel { Margin = new Thickness(14, 10, 14, 4) };
        var close = new Button { Content = Strings.PreviewClose, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(10, 0, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(close, "PreviewClose");
        close.Click += (_, _) => Close(); DockPanel.SetDock(close, Dock.Right); bar.Children.Add(close); bar.Children.Add(_caption);
        DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar); root.Children.Add(_image);
        PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
            if (!e.IsRepeat && Settings.Current.QuickPreviewEnabled && HotKey.Matches(Settings.Current.PreviewHotKey, key, Keyboard.Modifiers))
            { e.Handled = true; Close(); }
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        Closed += (_, _) => { _closed = true; _loadGeneration++; _image.Source = null; if (_open == this) _open = null; Theme.Changed -= Paint; Strings.LanguageChanged -= CloseForLanguage; };
    }
    private void CloseForLanguage() => Close();
    private void Paint() { Foreground = (Brush)Application.Current.Resources["MenuFg"]; }
    public async Task LoadImage(string path)
    {
        int generation = ++_loadGeneration;
        _image.Source = null; _caption.Text = Strings.PreviewLoading;
        var thumb = await Task.Run(() => Thumbnails.Load(path, 2048, 1));
        if (_closed || generation != _loadGeneration || !Settings.Current.QuickPreviewEnabled) return;
        _image.Source = thumb?.Image;
        _caption.Text = thumb is { } t ? $"{Path.GetFileName(path)} · {t.PixelWidth} × {t.PixelHeight}" : Strings.PreviewFailed;
    }
}
