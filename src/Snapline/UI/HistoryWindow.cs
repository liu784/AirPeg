using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Automation;
using Snapline.Core;

namespace Snapline.UI;

public sealed class HistoryWindow : Window
{
    private static HistoryWindow? _open;
    private readonly HistoryStore _store;
    private readonly Func<HistoryEntry, Task> _restore;
    private readonly ListBox _list = new() { SelectionMode = SelectionMode.Extended };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly StackPanel _actions = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 8) };
    private bool _closed;
    public static void Open(HistoryStore store, Func<HistoryEntry, Task> restore)
    {
        if (!store.Enabled) return;
        if (_open is not null) { _open.Activate(); return; }
        _open = new HistoryWindow(store, restore); _open.Show();
    }
    public static void CloseCurrent() => _open?.Close();
    public HistoryWindow(HistoryStore store, Func<HistoryEntry, Task> restore)
    {
        _store = store; _restore = restore;
        Title = Strings.HistoryTitle; Width = 720; Height = 570; MinWidth = 480; MinHeight = 360;
        MaxHeight = SystemParameters.WorkArea.Height - 40;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Icon = Brand.Icon; FontSize = 13;
        Paint(); Theme.Changed += Paint; Strings.LanguageChanged += CloseForLanguage;
        var root = new DockPanel { Margin = new Thickness(18) }; GlassWindow.Attach(this, root);
        var intro = new TextBlock { Text = Strings.HistoryPolicy, TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(intro, Dock.Top); root.Children.Add(intro);
        DockPanel.SetDock(_actions, Dock.Top); root.Children.Add(_actions);
        AddAction(Strings.HistoryRestore, "HistoryRestore", async () =>
        {
            if (_list.SelectedItems.Count != 1 || _list.SelectedItem is not ListBoxItem { Tag: HistoryEntry entry }) return;
            await _restore(entry); _status.Text = Strings.HistoryRestored;
        });
        AddAction(Strings.HistoryDelete, "HistoryDelete", async () =>
        {
            var entries = _list.SelectedItems.Cast<ListBoxItem>().Select(x => (HistoryEntry)x.Tag).ToArray();
            if (entries.Length == 0 || !Confirm(string.Format(Strings.HistoryDeleteConfirm, entries.Length))) return;
            await Task.Run(() => { foreach (var entry in entries) _store.Delete(entry.Id); });
            await Reload();
        });
        AddAction(Strings.HistoryClear, "HistoryClear", async () =>
        {
            if (!Confirm(Strings.HistoryClearConfirm)) return;
            await Task.Run(_store.Clear); await Reload();
        });
        AddAction(Strings.HistoryRefresh, "HistoryRefresh", Reload);
        DockPanel.SetDock(_status, Dock.Bottom); root.Children.Add(_status);
        AutomationProperties.SetAutomationId(_list, "HistoryList"); root.Children.Add(_list);
        Loaded += async (_, _) => await Run(Reload);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        Closed += (_, _) => { _closed = true; if (_open == this) _open = null; Theme.Changed -= Paint; Strings.LanguageChanged -= CloseForLanguage; };
    }
    private void CloseForLanguage() => Close();
    private void Paint() { Foreground = (System.Windows.Media.Brush)Application.Current.Resources["MenuFg"]; }
    private bool Confirm(string text) => GlassDialog.Show(this, text, Strings.HistoryTitle, confirm: true);
    private void AddAction(string text, string id, Func<Task> action)
    {
        var button = new Button { Content = text, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 0) };
        AutomationProperties.SetAutomationId(button, id); button.Click += async (_, _) => await Run(action); _actions.Children.Add(button);
    }
    private async Task Run(Func<Task> action)
    {
        _actions.IsEnabled = false;
        try { if (_store.Enabled) await action(); }
        catch (Exception ex) { Log.Error($"History action failed: {ex.Message}"); _status.Text = Strings.HistoryFailed; }
        finally { if (!_closed) _actions.IsEnabled = true; }
    }
    public async Task Reload()
    {
        var rows = await Task.Run(() => _store.List().Select(e => (entry: e, thumb: _store.Thumbnail(e.Id))).ToArray());
        if (_closed || !_store.Enabled) return;
        _list.Items.Clear();
        foreach (var row in rows)
        {
            var panel = new DockPanel { Margin = new Thickness(4), LastChildFill = true };
            var image = new Image { Source = row.thumb?.Image, Width = 136, Height = 86, Margin = new Thickness(0, 0, 14, 0), Stretch = System.Windows.Media.Stretch.Uniform };
            panel.Children.Add(image);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = row.entry.Name, TextWrapping = TextWrapping.Wrap });
            text.Children.Add(new TextBlock { Text = $"{row.entry.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss} · {row.entry.Bytes / 1024.0:N0} KB", Opacity = 0.65, Margin = new Thickness(0, 6, 0, 0) });
            panel.Children.Add(text); _list.Items.Add(new ListBoxItem { Content = panel, Tag = row.entry });
        }
        _status.Text = rows.Length == 0 ? Strings.HistoryEmpty : string.Format(Strings.HistoryCount, rows.Length);
    }
}
