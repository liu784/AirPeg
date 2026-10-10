using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Data;
using System.Windows.Automation;
using System.Windows.Threading;
using Microsoft.Win32;
using Snapline.Core;
using Snapline.Interop;
namespace Snapline.UI;

/// <summary>Settings use independent live desktop GPU glass; other chrome keeps its existing backdrop.
/// Foreground controls and actual screenshots remain outside the optical pipeline.</summary>
public sealed class GlassWindow
{
    private readonly Window _window;
    private readonly bool _textSafe;
    private readonly Grid _root = new();
    private readonly Rectangle _back = new() { IsHitTestVisible=false };
    private readonly Border _optics = new() { IsHitTestVisible=false };
    private readonly Border _edge = new() { IsHitTestVisible=false, BorderThickness=new Thickness(1), CornerRadius=new CornerRadius(12) };
    private BitmapSource? _snapshot;
    private ImageBrush? _snapshotBrush;
    private ChromeGlassEffect? _effect;
    private System.Drawing.Rectangle _bounds;
    private bool _native;
    private nint _hwnd;
    private LiveGlassBackdrop? _live;
    private bool _liveAttempted, _wasEnabled, _closed, _sessionLocked;
    private int _recoveryAttempts;
    private string? _lastLiveFailure;
    private readonly DispatcherTimer _recovery = new(DispatcherPriority.Background);
    private static readonly DependencyProperty OwnerProperty = DependencyProperty.RegisterAttached("BackdropOwner", typeof(GlassWindow), typeof(GlassWindow));
    public static string GetMode(Window window) => (window.GetValue(OwnerProperty) as GlassWindow)?.Mode ?? "none";
    public static string GetLiveDiagnostics(Window window) => (window.GetValue(OwnerProperty) as GlassWindow)?._live?.Diagnostics ?? "no-live-context";
    public static int GetLiveContextCount() => LiveGlassBackdrop.ContextCount;
    public static long GetLiveFrames(Window window) => (window.GetValue(OwnerProperty) as GlassWindow)?._live?.Frames ?? 0;
    public string Mode { get; private set; } = "low-effects";
    public static GlassWindow Attach(Window window, UIElement content, bool textSafe = false) => new(window, content, textSafe);
    private GlassWindow(Window window, UIElement content, bool textSafe)
    {
        _window=window; _textSafe=textSafe; _root.ClipToBounds=true;
        window.SetValue(OwnerProperty,this);
        RenderOptions.SetBitmapScalingMode(_back,BitmapScalingMode.HighQuality);
        _optics.Child=_back; _root.Children.Add(_optics);
        if(window.WindowStyle!=WindowStyle.None)
        {
            window.WindowStyle=WindowStyle.None;
            WindowChrome.SetWindowChrome(window,new WindowChrome { CaptionHeight=36,ResizeBorderThickness=new Thickness(window.ResizeMode==ResizeMode.NoResize?0:4),GlassFrameThickness=new Thickness(0),CornerRadius=new CornerRadius(12),UseAeroCaptionButtons=false });
            var frame=new DockPanel();var caption=new DockPanel { Height=36,Margin=new Thickness(12,0,5,0) };
            var actions=new StackPanel { Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center }; DockPanel.SetDock(actions,Dock.Right);caption.Children.Add(actions);
            WindowChrome.SetIsHitTestVisibleInChrome(actions,true);
            void Action(string geometry,string name,Action click)
            {
                var icon=new System.Windows.Shapes.Path { Data=Geometry.Parse(geometry),StrokeThickness=1.2,Width=12,Height=12,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center };
                icon.SetResourceReference(Shape.StrokeProperty,"ChromeText");
                var button=new Button { Content=icon,Width=32,Height=26,MinHeight=26,Padding=new Thickness(7),Margin=new Thickness(2,0,0,0),ToolTip=name,Background=Brushes.Transparent,BorderThickness=new Thickness(0) };
                AutomationProperties.SetName(button,name);button.Click+=(_,_)=>click();actions.Children.Add(button);
            }
            if(window.ResizeMode!=ResizeMode.NoResize) Action("M0,6 L12,6",Strings.MinimizeWindow,()=>SystemCommands.MinimizeWindow(window));
            if(window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip) Action("M0,0 H12 V12 H0 Z",Strings.MaximizeWindow,()=>{if(window.WindowState==WindowState.Maximized)SystemCommands.RestoreWindow(window);else SystemCommands.MaximizeWindow(window);});
            Action("M0,0 L12,12 M0,12 L12,0",Strings.CloseWindow,()=>SystemCommands.CloseWindow(window));
            var title=new TextBlock { FontSize=12,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis };
            title.SetBinding(TextBlock.TextProperty,new Binding(nameof(Window.Title)){Source=window});caption.Children.Add(title);DockPanel.SetDock(caption,Dock.Top);frame.Children.Add(caption);frame.Children.Add(content);_root.Children.Add(frame);
        }
        else _root.Children.Add(content);
        _root.Children.Add(_edge);
        window.Content=_root;
        window.FontFamily=new FontFamily("Segoe UI Variable Text, Microsoft YaHei UI, Segoe UI");
        window.SetResourceReference(Control.ForegroundProperty,"ChromeText");
        window.SourceInitialized += Initialize;
        window.LocationChanged += Position; window.SizeChanged += Position;
        Theme.Changed += Refresh;
        _recovery.Tick += RetryLive;
        if (_textSafe) { SystemEvents.DisplaySettingsChanged += DisplayChanged; SystemEvents.SessionSwitch += SessionChanged; window.StateChanged += RecoveryVisibility; }
        window.Closed += (_,_) => {
            _closed=true; _recovery.Stop(); _recovery.Tick-=RetryLive;
            if (_textSafe) { SystemEvents.DisplaySettingsChanged-=DisplayChanged; SystemEvents.SessionSwitch-=SessionChanged; window.StateChanged-=RecoveryVisibility; }
            Theme.Changed-=Refresh; window.LocationChanged-=Position; window.SizeChanged-=Position;
            _live?.Dispose(); _live=null; window.ClearValue(OwnerProperty);
            _snapshot=null;_snapshotBrush=null;_effect=null;_back.Fill=null;_back.Effect=null;_optics.Effect=null; FullScreen.Own.Remove(_hwnd);
        };
        Refresh();
    }
    private void Initialize(object? sender, EventArgs e)
    {
        _hwnd=new WindowInteropHelper(_window).Handle;
        FullScreen.Own.Add(_hwnd);
        // Settings create an excluded live GPU context on enable; other chrome retains its existing route.
        if (!_textSafe && GlassPalette.Enabled && !OperatingSystem.IsWindowsVersionAtLeast(10,0,22621)) CaptureBeforeShow();
        Refresh();
        Log.Notice($"Glass backdrop: {Mode}; renderTier={RenderCapability.Tier>>16}; density={Settings.Current.GlassIntensity:0}");
    }
    private void CaptureBeforeShow()
    {
        // One capture BEFORE this window is visible: no recursive feedback, timer, disk file or capture exclusion.
        try
        {
            _bounds=System.Windows.Forms.SystemInformation.VirtualScreen;
            if (_bounds.Width<=0 || _bounds.Height<=0 || (long)_bounds.Width*_bounds.Height>16_000_000) return;
            using var bitmap=new System.Drawing.Bitmap(_bounds.Width,_bounds.Height,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using(var graphics=System.Drawing.Graphics.FromImage(bitmap)) graphics.CopyFromScreen(_bounds.Location,System.Drawing.Point.Empty,_bounds.Size);
            if (_textSafe) { _snapshot=DesktopColorField.Create(bitmap,9,6); return; }
            var handle=bitmap.GetHbitmap();
            try { _snapshot=Imaging.CreateBitmapSourceFromHBitmap(handle,0,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions()); _snapshot.Freeze(); }
            finally { DeleteObject(handle); }
        }
        catch(Exception ex) { Log.Notice($"Glass snapshot unavailable: {ex.Message}"); _snapshot=null; }
    }
    private void Position(object? sender, EventArgs e) => UpdateSnapshot();
    private void UpdateSnapshot()
    {
        if (_snapshot is null || _native || _live is not null || !GlassPalette.Enabled || _hwnd==0 || _root.ActualWidth<1 || _root.ActualHeight<1) return;
        var origin=_root.PointToScreen(new Point()); var dpi=VisualTreeHelper.GetDpi(_root);
        _snapshotBrush ??= new ImageBrush(_snapshot) { ViewboxUnits=_textSafe?BrushMappingMode.RelativeToBoundingBox:BrushMappingMode.Absolute, Stretch=Stretch.Fill, TileMode=TileMode.None };
        var view=new Rect(origin.X-_bounds.Left,origin.Y-_bounds.Top,_root.ActualWidth*dpi.DpiScaleX,_root.ActualHeight*dpi.DpiScaleY);
        // The atmosphere source is at most 9x6, not a desktop-sized bitmap. Preserve virtual-screen/DPI mapping.
        _snapshotBrush.Viewbox=_textSafe?new Rect(view.X/_bounds.Width,view.Y/_bounds.Height,view.Width/_bounds.Width,view.Height/_bounds.Height):view;
        _back.Fill=_snapshotBrush;
        if (_optics.Effect is ChromeGlassEffect effect) effect.Size=new Point(_root.ActualWidth,_root.ActualHeight);
    }
    private void RecoveryVisibility(object? sender, EventArgs e)
    {
        if (_window.WindowState == WindowState.Minimized) _recovery.Stop();
        else if (_lastLiveFailure is not null) QueueRecovery(_lastLiveFailure);
    }
    private void QueueRecovery(string message)
    {
        _lastLiveFailure=message;
        string m=message.ToLowerInvariant();
        bool transient=new[]{"887a0026","887a0005","887a0006","887a0007","887a0022","887a0028"}.Any(m.Contains);
        if (!transient || _closed || _sessionLocked || !GlassPalette.Enabled || !_window.IsVisible ||
            _window.WindowState==WindowState.Minimized || _recoveryAttempts>=3 || _recovery.IsEnabled) return;
        _recovery.Interval=TimeSpan.FromSeconds(new[]{1,3,10}[_recoveryAttempts]);
        _recovery.Start();
        Log.Notice($"Live glass recovery scheduled: attempt {_recoveryAttempts+1}/3");
    }
    private void RetryLive(object? sender, EventArgs e)
    {
        _recovery.Stop();
        if (_closed || _sessionLocked || !GlassPalette.Enabled || !_window.IsVisible || _window.WindowState==WindowState.Minimized) return;
        _recoveryAttempts++; _liveAttempted=false; Refresh();
    }
    private void RebuildLive()
    {
        if (_closed) return;
        _recovery.Stop(); _live?.Dispose(); _live=null; _liveAttempted=false; _recoveryAttempts=0;
        Refresh();
    }
    private void DisplayChanged(object? sender, EventArgs e)
    {
        if (!_closed) _window.Dispatcher.BeginInvoke((Action)RebuildLive);
    }
    private void SessionChanged(object sender, SessionSwitchEventArgs e)
    {
        if (_closed) return;
        _window.Dispatcher.BeginInvoke((Action)(() => {
            if (_closed) return;
            if (e.Reason==SessionSwitchReason.SessionLock || e.Reason==SessionSwitchReason.RemoteDisconnect) {
                _sessionLocked=true; _recovery.Stop(); _live?.Dispose(); _live=null; Refresh();
            }
            else if (e.Reason==SessionSwitchReason.SessionUnlock || e.Reason==SessionSwitchReason.RemoteConnect) {
                _sessionLocked=false; RebuildLive();
            }
        }));
    }
    private void Refresh()
    {
        bool enabled=GlassPalette.Enabled;
        if(!enabled) { _recovery.Stop(); _recoveryAttempts=0; _lastLiveFailure=null; _live?.Dispose(); _live=null; _liveAttempted=false; }
        else if(!_wasEnabled) { _liveAttempted=false; _recoveryAttempts=0; _lastLiveFailure=null; }
        _wasEnabled=enabled;
        if(enabled && !_sessionLocked && _textSafe && _hwnd!=0 && _live is null && !_liveAttempted)
        {
            _liveAttempted=true;
            _live=LiveGlassBackdrop.TryCreate(_root,_hwnd,out var creationFailure);
            if(_live is not null)
            {
                _live.Failed += message => { _live=null; Log.Notice("Live glass stopped safely: "+message); QueueRecovery(message); Refresh(); };
                _live.Start(); _lastLiveFailure=null;
            }
            else if (creationFailure is not null) QueueRecovery(creationFailure);
        }
        _native=false;
        if (!_textSafe && _hwnd!=0 && OperatingSystem.IsWindowsVersionAtLeast(10,0,22621))
        {
            int kind=enabled?3:1, dark=Theme.AppsDark?1:0;
            int hr=DwmSetWindowAttribute(_hwnd,38,ref kind,4);
            _native=enabled && hr>=0;
            DwmSetWindowAttribute(_hwnd,20,ref dark,4);
            var margins=new Margins { Left=_native?-1:0, Right=_native?-1:0, Top=_native?-1:0, Bottom=_native?-1:0 };
            DwmExtendFrameIntoClientArea(_hwnd,ref margins);
            if (HwndSource.FromHwnd(_hwnd)?.CompositionTarget is { } target) target.BackgroundColor=_native?Colors.Transparent:Colors.White;
        }
        _window.Background=_native?Brushes.Transparent:(Brush)Application.Current.Resources["ChromeBase"];
        _root.Background=Brushes.Transparent;
        _edge.SetResourceReference(Border.BorderBrushProperty, enabled?(_textSafe?"SettingsGlassEdge":"ChromeShine"):"ChromeEdge");
        _window.Resources.Remove("ChromeReadingPanel");
        _window.Resources.Remove("SettingsContentEdge");
        _window.Resources.Remove(typeof(TextBlock));
        foreach(var key in new[]{"ChromeControl","ChromeHover","ChromePressed","ChromeSelected","ChromeEdge"}) _window.Resources.Remove(key);
        _back.Fill=Brushes.Transparent; _back.Opacity=1;
        Mode=enabled?"material-fallback":"low-effects";
        if (_live is not null && enabled)
        {
            _back.Effect=null; _optics.Effect=null;
            _back.Fill=new ImageBrush(_live.Image) { Stretch=Stretch.Fill };
            Mode="live-desktop-gpu (D3D11 shared D3D9Ex)";
            // Clear glass is a continuous optical surface. No rectangular reading
            // mask, no luminance clamp or opaque backing behind the content page.
            _window.Resources["ChromeReadingPanel"]=Brushes.Transparent;
            _window.Resources["SettingsContentEdge"]=Brushes.Transparent;
            Brush ClearFill(double alpha) => new SolidColorBrush(Color.FromArgb((byte)Math.Round(255*alpha),255,255,255));
            _window.Resources["ChromeControl"]=ClearFill(.12+GlassPalette.Density*.10);
            _window.Resources["ChromeHover"]=ClearFill(.28);
            _window.Resources["ChromePressed"]=ClearFill(.38);
            _window.Resources["ChromeSelected"]=new SolidColorBrush(Color.FromArgb(60,120,175,238));
            _window.Resources["ChromeEdge"]=new SolidColorBrush(Color.FromArgb(90,255,255,255));
        }
        else if (_native)
        {
            _back.Effect=null; _optics.Effect=null; Mode="DWM-acrylic";
            var c=Theme.AppsDark?Color.FromRgb(18,26,37):Color.FromRgb(244,248,253);
            c.A=(byte)(100+GlassPalette.Density*95); _back.Fill=new SolidColorBrush(c);
        }
        else if (enabled && _snapshot is not null && (RenderCapability.Tier>>16)>0 && RenderCapability.IsPixelShaderVersionSupported(3,0))
        {
            _effect ??= new ChromeGlassEffect();
            _effect.Density=GlassPalette.Density; _effect.Dark=Theme.AppsDark?1:0; _effect.TextSafe=_textSafe?1:0;
            _optics.Effect=_effect;
            if(_back.Effect is not BlurEffect) _back.Effect=new BlurEffect { KernelType=KernelType.Gaussian, RenderingBias=RenderingBias.Quality };
            ((BlurEffect)_back.Effect).Radius=_textSafe?24+GlassPalette.Density*20:10+GlassPalette.Density*18;
            Mode=_textSafe?"atmosphere-gpu (9x6 averages, frozen until reopen)":"snapshot-gpu (frozen until reopen)"; UpdateSnapshot();
        }
        else {
            _back.Effect=null; _optics.Effect=null;
            if(enabled && _textSafe && _snapshot is not null) { Mode="readable-atmosphere-fallback"; _back.Opacity=.20; UpdateSnapshot(); }
            else if(enabled) _back.SetResourceReference(Shape.FillProperty,"ChromeControl");
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Margins { public int Left,Right,Top,Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window,int attribute,ref int value,int size);
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(nint window,ref Margins margins);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint handle);
}