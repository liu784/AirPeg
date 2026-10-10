using System.Windows;
using Snapline.Core;

namespace Snapline.UI;

/// <summary>
/// Application text in Simplified Chinese by default, with English as an explicit setting.
/// </summary>
public static class Strings
{
    public static event Action? LanguageChanged;

    /// <summary>"zh-CN" or "en". Legacy automatic or unsupported choices resolve to Chinese.</summary>
    public static string Language { get; private set; } = Resolve();

    public static bool IsRtl => false;
    public static FlowDirection Flow => IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    public static void Refresh()
    {
        var was = Language;
        Language = Resolve();
        if (was != Language) LanguageChanged?.Invoke();
    }

    private static string Resolve()
    {
        var chosen = Core.Settings.Current.Language;
        return string.Equals(chosen, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "zh-CN";
    }

    private static string L(string en) => Language == "zh-CN" && ZhCn.TryGetValue(en, out var zh) ? zh : en;

    public static string LiquidGlass => L("Liquid glass");
    public static string GlassIntensity => L("Glass intensity");
    public static string ResetAppearance => L("Restore default appearance");
    public static string GlassDescription => L("Clear glass preserves the background. Adjust optical softness without a solid content mask.");
    public static string CancelAction => L("Cancel");
    public static string ConfirmAction => L("Confirm");

    public static string CloseWindow => L("Close window");
    public static string MinimizeWindow => L("Minimize");
    public static string MaximizeWindow => L("Maximize or restore");

    public static string AppName => L("AirPeg");
    public static string CouldNotStart => L("{0} could not start.\n\n{1}\n\n{2}");
    public static string Tagline => L("Screenshots, hung out to dry.");

    public static string ShowLine => L("Show line");
    public static string HideLine => L("Hide line");
    public static string TakeEverythingDown => L("Take everything down");
    public static string NewCapture => L("New capture");
    public static string NewCaptureTip => L("Click to take a new screenshot (Win+Shift+S)");
    public static string CatchClipboard => L("Catch clipboard captures");
    public static string CatchClipboardTip => L("Win+Shift+S and PrtScn captures hang even when no file is saved");
    public static string StayDownWhileUnused => L("Stay down until each capture is used");
    public static string StayDownWhileUnusedTip => L("The line waits while a capture has not been copied, dragged or opened");
    public static string TakeDownAfterDrag => L("Take down after dragging into an app");
    public static string TakeDownAfterDragTip => L("A photo dropped into a chat or an app leaves the line, like one saved to a folder");
    public static string RevealAtTopEdge => L("Bring the line down when the pointer rests at the top edge");
    public static string RevealAtTopEdgeTip => L("Hold the pointer still at the top edge for the configured reveal delay. Off, use the shortcut or tray icon.");
    public static string OpenScreenshotsFolder => L("Open Screenshots folder");
    public static string OpenInboxFolder => L("Open caught captures folder");
    public static string Sounds => L("Sounds");
    public static string StartWithWindows => L("Start with Windows");
    public static string Settings => L("Settings…");
    public static string About => L("About AirPeg");
    public static string Quit => L("Quit AirPeg");

    public static string Copy => L("Copy");
    public static string CopyText => L("Copy text");
    public static string Open => L("Open");
    public static string Edit => L("Edit");
    public static string MarkUp => L("Mark up");
    public static string EditInPaint => L("Edit in Paint");
    public static string Pen => L("Pen");
    public static string Highlighter => L("Highlighter");
    public static string Circle => L("Circle");
    public static string Box => L("Box");
    public static string Arrow => L("Arrow");
    public static string TextTool => L("Text");
    public static string Blur => L("Blur");
    public static string NewNote => L("New note");
    public static string Menu => L("Menu");
    public static string EditNote => L("Edit note");
    public static string NoteColour => L("Note colour");
    public static string NotePlaceholder => L("Write a note…");
    public static string NoteName(string key) => L(key switch { "yellow" => "Yellow", "pink" => "Pink", "blue" => "Blue", "green" => "Green", "orange" => "Orange", _ => key });
    public static string Colour => L("Colour");
    public static string Thickness => L("Thickness");
    public static string Undo => L("Undo");
    public static string Redo => L("Redo");
    public static string Revert => L("Revert to original");
    public static string Done => L("Done");
    public static string Saved => L("Saved");
    public static string Reverted => L("Back to the original");
    public static string CouldNotSave => L("Could not save");
    public static string ShowInExplorer => L("Show in Explorer");
    public static string KeepOnLine => L("Keep on the line");
    public static string SaveToDesktop => L("Save to Desktop");
    public static string SaveToScreenshots => L("Save to Pictures\\Screenshots");
    public static string Discard => L("Discard");
    public static string TakeDown => L("Take down");
    public static string MoveToRecycleBin => L("Move to Recycle Bin");

    public static string Hint => L("Take a screenshot and it will hang here");
    public static string Copied => L("Copied");
    public static string TextCopied => L("Text copied");
    public static string JustNow => L("just now");
    public static string MinutesAgo => L("{0} min ago");
    public static string HoursAgo => L("{0} h ago");

    public static string HistoryTitle => L("Screenshot history");
    public static string HistoryEnabled => L("Save screenshot history");
    public static string HistoryPolicy => L("Save new captures as separate copies for 7 days, up to 100 images / 256 MB (64 MB each). Later edits do not update these copies. Off stops saving and access; existing history is kept until re-enabled and cleaned.");
    public static string HistoryRestore => L("Restore to line");
    public static string HistoryDelete => L("Delete selected history");
    public static string HistoryClear => L("Clear history");
    public static string HistoryRefresh => L("Refresh");
    public static string HistoryRestored => L("Restored as a new file. The history copy is unchanged.");
    public static string HistoryDeleteConfirm => L("Delete {0} history copies? Hanging captures and original files are not affected.");
    public static string HistoryClearConfirm => L("Permanently clear all history copies? Hanging captures and original files are not affected.");
    public static string HistoryEmpty => L("No history yet. New captures will appear here while history is enabled.");
    public static string HistoryCount => L("{0} captures · Ctrl/Shift selects multiple · Restore one at a time");
    public static string HistoryFailed => L("History could not complete the operation. Original captures are unchanged. Check storage permissions or the log.");
    public static string HistoryCaptureFailed => L("Could not save this history copy. The capture still works normally; it may exceed 64 MB or storage may be unavailable.");
    public static string QuickPreview => L("Quick preview");
    public static string PreviewSingleKeyReady => L("Single key available. Active only over a capture or inside its preview.");
    public static string PreviewShortcut => L("Preview shortcut");
    public static string PreviewShortcutTip => L("Click the field and press a single key or combination. Single keys work only while hovering over a capture or inside the preview, leaving normal typing unchanged elsewhere. Press again to close; Esc also closes.");
    public static string QuickPreviewEnabled => L("Enable quick preview");
    public static string QuickPreviewTip => L("Middle-click a capture or choose Quick preview from its menu. Esc closes it. Double-click and editing keep their usual actions.");
    public static string PreviewClose => L("Close (Esc)");
    public static string PreviewLoading => L("Loading preview…");
    public static string PreviewFailed => L("This image cannot be previewed. The file may be missing or unreadable.");
    public static string MiddleClick => L("Middle click");

    public static string SettingsTitle => L("Settings");
    public static string SectionLine => L("The line");
    public static string LockExpanded => L("Lock the line open");
    public static string UnlockExpanded => L("Unlock the line");
    public static string LockHangerEnabled => L("Lock ornament");
    public static string LockTip => L("Show the lock at the right end of the rope. Turn it on to lock the line open; click the ornament to lock or unlock. Off hides it and stops locking.");
    public static string FullScreenHide => L("Hide in full-screen apps");
    public static string FullScreenHideTip => L("Full screen takes priority over the lock. Return to the previous line state after leaving full screen. Ordinary maximized windows stay unchanged.");
    public static string ShortcutAlerts => L("Shortcut conflict reminders");
    public static string ShortcutAlertsTip => L("Show a reminder when registration fails. Off, shortcuts still never replace another app's registration.");
    public static string ShortcutUnavailable => L("Shortcut unavailable");
    public static string ShortcutReady => L("Shortcut registered successfully.");
    public static string ShortcutOccupied => L("{0} is already in use. Click the shortcut field and press a different combination.");
    public static string ShortcutReserved => L("Invalid or Windows-reserved shortcut. Use a different Ctrl, Alt or Shift combination; Print Screen, F12 and Windows-key shortcuts are reserved.");
    public static string ShortcutFailed => L("Shortcut registration failed (error {0}). Choose another combination.");
    public static string SectionDelays => L("Trigger delays");
    public static string RevealDelay => L("Reveal delay");
    public static string HideDelay => L("Hide delay");
    public static string DelayTip => L("0 seconds is immediate; up to 3 seconds. Animation speeds stay unchanged.");
    public static string PreviewDelays => L("Preview reveal and hide");
    public static string SectionAnimation => L("Animation speed");
    public static string ExpandSpeed => L("Line reveal speed");
    public static string HideSpeed => L("Line hide speed");
    public static string ResetSpeeds => L("Restore default speeds");
    public static string AnimationSpeedTip => L("0.5× is slower; 2.0× is faster. Applies to the next transition.");
    public static string SectionLook => L("Look");
    public static string SectionCaptures => L("Captures");
    public static string SectionGeneral => L("General");
    public static string Shortcut => L("Show or hide the line");
    public static string ShortcutTip => L("Click here and press the keys you want, like Ctrl+Alt+T");
    public static string LineDistance => L("Distance from the top of the screen");
    public static string LineDistanceTip => L("You can also drag the line itself up or down");
    public static string RopeColor => L("Line colour");
    public static string PegStyle => L("Clothespins");
    public static string Bows => L("Bows at the ends of the line");
    public static string AutoArrange => L("Arrange photos evenly along the line");
    public static string AutoArrangeTip => L("Off, each photo stays where you put it");
    public static string Appearance => L("Appearance");
    public static string LanguageLabel => L("Language");
    public static string Auto => L("Follow Windows");
    public static string Light => L("Light");
    public static string Dark => L("Dark");
    public static string CaughtCapturesFolder => L("Caught captures are saved to");
    public static string Change => L("Change…");
    public static string InboxFolderRefused => L("Caught captures need a folder of their own, because discarding one sends it to the Recycle Bin. Pick a folder that is not your Desktop, Pictures, Screenshots or a watched folder, and not a parent of them.");
    public static string WatchFoldersLabel => L("Folders watched for new screenshots");
    public static string AddFolder => L("Add folder…");
    public static string RemoveFolder => L("Remove");
    public static string SettingsFooter => L("Changes apply right away. Everything lives in {0}.");

    public static string WelcomeTitle => L("AirPeg is on the line");
    public static string WelcomeBody => L("Take a screenshot and it hangs at the top of the screen. Rest the pointer against the top edge to bring the line down, or press {0}.");

    public static string StorageInfo => L("Caught captures live in\n{0}\n\nSettings: {1}");

    public static string AboutBody => L(
        "Click a photo to copy it. Press and hold to edit it. Double click to open it.\n" +
        "Drag it along the line to reorder, into an app to send a copy, into a folder to keep it, or to the Recycle Bin to let it go.\n" +
        "Rest the pointer against the top edge of the screen, or press {0}, to bring the line down.");

    // Colour and peg names
    public static string ColorName(string key) => L(key switch
    {
        "bronze" => "Bronze", "gray" => "Gray", "black" => "Black", "white" => "White", "red" => "Red",
        "blue" => "Blue", "green" => "Green", "gold" => "Gold", "pink" => "Pink", "purple" => "Purple", _ => key,
    });

    public static string PegName(string key) => L(key switch
    {
        "wood" => "Wooden", "metal" => "Aluminium", "mixed" => "Coloured plastic, mixed", "red" => "Red plastic",
        "blue" => "Blue plastic", "green" => "Green plastic", "yellow" => "Yellow plastic", _ => key,
    });

    private static readonly Dictionary<string, string> ZhCn = new()
    {
        ["Screenshot history"] = "截图历史收纳盒",
        ["Save screenshot history"] = "保存截图历史",
        ["Save new captures as separate copies for 7 days, up to 100 images / 256 MB (64 MB each). Later edits do not update these copies. Off stops saving and access; existing history is kept until re-enabled and cleaned."] = "保存开启后的新截图副本，保留7天，最多100张／256 MB（单张64 MB）。后续编辑不会同步到副本。关闭后停止记录和访问，已有历史保留，重新开启时清理过期副本。",
        ["Restore to line"] = "恢复到晾衣绳",
        ["Delete selected history"] = "删除所选历史",
        ["Clear history"] = "清空历史",
        ["Refresh"] = "刷新",
        ["Restored as a new file. The history copy is unchanged."] = "已恢复为新文件，历史副本保持不变。",
        ["Delete {0} history copies? Hanging captures and original files are not affected."] = "删除所选的 {0} 个历史副本？不会删除悬挂截图或原文件。",
        ["Permanently clear all history copies? Hanging captures and original files are not affected."] = "永久清空全部历史副本？不会删除悬挂截图或原文件。",
        ["No history yet. New captures will appear here while history is enabled."] = "暂无历史。开启后捕获的新截图会显示在这里。",
        ["{0} captures · Ctrl/Shift selects multiple · Restore one at a time"] = "{0} 张截图 · Ctrl／Shift 可多选 · 每次恢复一张",
        ["History could not complete the operation. Original captures are unchanged. Check storage permissions or the log."] = "历史操作未完成，原截图未改动。请检查存储权限或日志。",
        ["Could not save this history copy. The capture still works normally; it may exceed 64 MB or storage may be unavailable."] = "此次历史副本未保存，截图仍可正常使用；可能超过64 MB或存储不可用。",
        ["Single key available. Active only over a capture or inside its preview."] = "单键可用，仅在悬停截图或预览窗口内生效。",
        ["Preview shortcut"] = "大图预览快捷键",
        ["Click the field and press a single key or combination. Single keys work only while hovering over a capture or inside the preview, leaving normal typing unchanged elsewhere. Press again to close; Esc also closes."] = "点击输入框后按下单键或组合键。单键仅在悬停截图或预览窗口内生效，其他位置可正常打字；再按一次关闭，Esc也可关闭。",
        ["Quick preview"] = "快速大图预览",
        ["Enable quick preview"] = "启用快速大图预览",
        ["Middle-click a capture or choose Quick preview from its menu. Esc closes it. Double-click and editing keep their usual actions."] = "中键点击截图或从右键菜单选择快速大图预览，Esc关闭。双击和编辑仍使用原有操作。",
        ["Close (Esc)"] = "关闭（Esc）",
        ["Loading preview…"] = "正在加载预览…",
        ["This image cannot be previewed. The file may be missing or unreadable."] = "无法预览此图片，文件可能已移走或无法读取。",
        ["Middle click"] = "鼠标中键",

        ["Hide in full-screen apps"] = "全屏智能隐藏",
        ["Full screen takes priority over the lock. Return to the previous line state after leaving full screen. Ordinary maximized windows stay unchanged."] = "全屏时优先隐藏，即使已锁定。退出全屏后恢复先前状态，普通最大化窗口不受影响。",
        ["Shortcut conflict reminders"] = "快捷键冲突提醒",
        ["Show a reminder when registration fails. Off, shortcuts still never replace another app's registration."] = "注册失败时显示提醒。关闭后仍不会抢占其他软件的快捷键。",
        ["Shortcut unavailable"] = "快捷键不可用",
        ["Shortcut registered successfully."] = "快捷键注册成功。",
        ["{0} is already in use. Click the shortcut field and press a different combination."] = "{0} 已被占用。点击快捷键输入框，按下其他组合即可重新指定。",
        ["Invalid or Windows-reserved shortcut. Use a different Ctrl, Alt or Shift combination; Print Screen, F12 and Windows-key shortcuts are reserved."] = "组合无效或为系统保留。请使用其他 Ctrl、Alt 或 Shift 组合；PrtScn、F12 及 Windows 键组合不注册。",
        ["Shortcut registration failed (error {0}). Choose another combination."] = "快捷键注册失败（错误 {0}）。请选择其他组合。",
        ["Lock the line open"] = "锁定展开",
        ["Unlock the line"] = "解除锁定",
        ["Lock ornament"] = "锁定展开挂件",
        ["Show the lock at the right end of the rope. Turn it on to lock the line open; click the ornament to lock or unlock. Off hides it and stops locking."] = "在绳最右端显示锁饰。开启时锁定展开，点击挂件可锁定或解锁；关闭后隐藏挂件并停止锁定。",
        ["Trigger delays"] = "触发延迟",
        ["Reveal delay"] = "晾衣绳展开延迟",
        ["Hide delay"] = "晾衣绳隐藏延迟",
        ["0 seconds is immediate; up to 3 seconds. Animation speeds stay unchanged."] = "0 秒为立即触发，最多 3 秒。动画速度保持不变。",
        ["Preview reveal and hide"] = "预览展开与隐藏",
        ["Animation speed"] = "动画速度",
        ["Line reveal speed"] = "晾衣绳展开速度",
        ["Line hide speed"] = "晾衣绳隐藏速度",
        ["Restore default speeds"] = "恢复默认速度",
        ["0.5× is slower; 2.0× is faster. Applies to the next transition."] = "0.5× 较慢，2.0× 较快。下次展开或隐藏时生效。",
        ["Caught captures live in\n{0}\n\nSettings: {1}"] = "捕获截图保存在\n{0}\n\n设置文件：{1}",
        ["Screenshots, hung out to dry."] = "把截图挂上晾衣绳。",
        ["Show line"] = "展开晾衣绳",
        ["Hide line"] = "收起晾衣绳",
        ["Take everything down"] = "全部取下",
        ["New capture"] = "新建截图",
        ["Click to take a new screenshot (Win+Shift+S)"] = "点击截图（Win+Shift+S）",
        ["Catch clipboard captures"] = "捕获剪贴板截图",
        ["Win+Shift+S and PrtScn captures hang even when no file is saved"] = "通过 Win+Shift+S 或 PrtScn 截取的图片，即使没有保存为文件也会挂上晾衣绳",
        ["Stay down until each capture is used"] = "在每张截图使用前保持展开",
        ["The line waits while a capture has not been copied, dragged or opened"] = "还有截图未被复制、拖出或打开时，晾衣绳保持展开",
        ["Take down after dragging into an app"] = "拖入应用后取下截图",
        ["A photo dropped into a chat or an app leaves the line, like one saved to a folder"] = "截图拖入聊天窗口或其他应用后，会像保存到文件夹一样离开晾衣绳",
        ["Bring the line down when the pointer rests at the top edge"] = "鼠标停在屏幕顶边时展开",
        ["Hold the pointer still at the top edge for the configured reveal delay. Off, use the shortcut or tray icon."] = "鼠标停在顶边达到设定的展开延迟后显示。关闭后，可用快捷键或托盘图标展开。",
        ["Open Screenshots folder"] = "打开系统截图文件夹",
        ["Open caught captures folder"] = "打开捕获截图文件夹",
        ["Caught captures need a folder of their own, because discarding one sends it to the Recycle Bin. Pick a folder that is not your Desktop, Pictures, Screenshots or a watched folder, and not a parent of them."] = "捕获的截图需要独立文件夹，因为丢弃截图会将其移入回收站。请选择桌面、图片、系统截图及监控文件夹以外的位置，也不要选择包含这些文件夹的上级目录。",
        ["Sounds"] = "声音",
        ["Start with Windows"] = "开机启动",
        ["Settings…"] = "设置…",
        ["AirPeg"] = "轻挂",
        ["About AirPeg"] = "关于轻挂",
        ["Quit AirPeg"] = "退出轻挂",
        ["Copy"] = "复制",
        ["Copy text"] = "复制文字",
        ["Open"] = "打开",
        ["Edit"] = "编辑",
        ["Mark up"] = "标注",
        ["Edit in Paint"] = "在画图中编辑",
        ["Pen"] = "画笔",
        ["Highlighter"] = "荧光笔",
        ["Circle"] = "圆形",
        ["Box"] = "矩形",
        ["Arrow"] = "箭头",
        ["Text"] = "文字",
        ["Blur"] = "模糊",
        ["New note"] = "新建便笺",
        ["Menu"] = "菜单",
        ["Edit note"] = "编辑便笺",
        ["Note colour"] = "便笺颜色",
        ["Write a note…"] = "写点什么…",
        ["Yellow"] = "黄色",
        ["Orange"] = "橙色",
        ["Colour"] = "颜色",
        ["Thickness"] = "粗细",
        ["Undo"] = "撤销",
        ["Redo"] = "重做",
        ["Revert to original"] = "恢复原图",
        ["Done"] = "完成",
        ["Saved"] = "已保存",
        ["Back to the original"] = "已恢复原图",
        ["Could not save"] = "保存失败",
        ["Show in Explorer"] = "在文件资源管理器中显示",
        ["Keep on the line"] = "保留在晾衣绳上",
        ["Save to Desktop"] = "保存到桌面",
        ["Save to Pictures\\Screenshots"] = "保存到系统截图文件夹",
        ["Discard"] = "丢弃",
        ["Take down"] = "取下",
        ["Move to Recycle Bin"] = "移到回收站",
        ["Take a screenshot and it will hang here"] = "截一张图，就会挂在这里",
        ["Copied"] = "已复制",
        ["Text copied"] = "已复制文字",
        ["just now"] = "刚刚",
        ["{0} min ago"] = "{0} 分钟前",
        ["{0} h ago"] = "{0} 小时前",
        ["Settings"] = "设置",
        ["The line"] = "晾衣绳",
        ["Look"] = "外观",
        ["Captures"] = "截图",
        ["General"] = "通用",
        ["Show or hide the line"] = "展开或收起晾衣绳",
        ["Click here and press the keys you want, like Ctrl+Alt+T"] = "点击此处并按下快捷键，例如 Ctrl+Alt+T",
        ["Distance from the top of the screen"] = "距屏幕顶部的距离",
        ["You can also drag the line itself up or down"] = "也可以上下拖动晾衣绳来调整位置",
        ["Line colour"] = "晾衣绳颜色",
        ["Clothespins"] = "夹子样式",
        ["Bows at the ends of the line"] = "晾衣绳两端显示蝴蝶结",
        ["Arrange photos evenly along the line"] = "在晾衣绳上均匀排列截图",
        ["Off, each photo stays where you put it"] = "关闭后，截图保留在拖放的位置",
        ["Appearance"] = "外观模式",
        ["Language"] = "语言",
        ["Follow Windows"] = "跟随 Windows",
        ["Light"] = "浅色",
        ["Dark"] = "深色",
        ["Caught captures are saved to"] = "捕获截图的保存位置",
        ["Change…"] = "更改…",
        ["Folders watched for new screenshots"] = "监控新截图的文件夹",
        ["Add folder…"] = "添加文件夹…",
        ["Remove"] = "移除",
        ["Changes apply right away. Everything lives in {0}."] = "更改立即生效。设置保存在 {0}。",
        ["AirPeg is on the line"] = "轻挂已就绪",
        ["Take a screenshot and it hangs at the top of the screen. Rest the pointer against the top edge to bring the line down, or press {0}."] = "截一张图，它就会挂在屏幕上方。将鼠标停在屏幕顶边，或按 {0} 展开晾衣绳。",
        ["Click a photo to copy it. Press and hold to edit it. Double click to open it.\nDrag it along the line to reorder, into an app to send a copy, into a folder to keep it, or to the Recycle Bin to let it go.\nRest the pointer against the top edge of the screen, or press {0}, to bring the line down."] = "单击截图可复制，长按可编辑，双击可打开。\n沿晾衣绳拖动可调整顺序；拖入应用可发送副本，拖入文件夹可保存，拖入回收站可丢弃。\n将鼠标停在屏幕顶边，或按 {0} 展开晾衣绳。",
        ["Bronze"] = "铜色",
        ["Gray"] = "灰色",
        ["Black"] = "黑色",
        ["White"] = "白色",
        ["Red"] = "红色",
        ["Blue"] = "蓝色",
        ["Green"] = "绿色",
        ["Gold"] = "金色",
        ["Pink"] = "粉色",
        ["Purple"] = "紫色",
        ["Wooden"] = "木夹",
        ["Aluminium"] = "铝制夹",
        ["Coloured plastic, mixed"] = "混合彩色塑料夹",
        ["Red plastic"] = "红色塑料夹",
        ["Blue plastic"] = "蓝色塑料夹",
        ["Green plastic"] = "绿色塑料夹",
        ["Yellow plastic"] = "黄色塑料夹",
        ["{0} could not start.\n\n{1}\n\n{2}"] = "{0} 无法启动。\n\n{1}\n\n{2}",
        ["Liquid glass"] = "液态玻璃效果",
        ["Glass intensity"] = "玻璃浓度",
        ["Restore default appearance"] = "恢复默认外观",
        ["Clear glass preserves the background. Adjust optical softness without a solid content mask."] = "清透玻璃保留背景。调节光学柔化，不叠加内容遮罩。",
        ["Cancel"] = "取消",
        ["Confirm"] = "确认",
        ["Close window"] = "关闭窗口",
        ["Minimize"] = "最小化",
        ["Maximize or restore"] = "最大化或还原",
    };
}
