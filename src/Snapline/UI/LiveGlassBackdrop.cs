using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Snapline.Core;

namespace Snapline.UI;

/// <summary>Own-window-only, live DXGI desktop glass through an independent GPU shared surface.</summary>
internal sealed class LiveGlassBackdrop : IDisposable
{
    private readonly FrameworkElement _root;
    private readonly nint _hwnd;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
    private nint _renderer, _surface, _monitor;
    private int _width, _height;
    private long _lastTick;
    private int _idleTicks;
    private readonly Window? _window;
    private bool _active, _disposed, _imageLocked, _publishSurface, _surfaceReady;
    public D3DImage Image { get; } = new();
    public long Frames { get; private set; }
    internal static int ContextCount => Native._library == 0 ? 0 : Native.ContextCount();
    private long _ticks, _lockMisses, _nativeCalls;
    public string Diagnostics => $"ticks={_ticks}, lockMisses={_lockMisses}, nativeCalls={_nativeCalls}, pendingLock={_imageLocked}, front={Image.IsFrontBufferAvailable}, ready={_surfaceReady}, rootVisible={_root.IsVisible}, {(_renderer == 0 ? "disposed" : Native.Stats(_renderer))}";
    public event Action<string>? Failed;
    public static LiveGlassBackdrop? TryCreate(FrameworkElement root, nint hwnd, out string? failure)
    {
        try { failure = null; return new(root, hwnd); }
        catch (Exception ex) { failure = ex.Message; Log.Notice("Live glass unavailable: " + ex.Message); return null; }
    }
    private LiveGlassBackdrop(FrameworkElement root, nint hwnd)
    {
        _root = root; _hwnd = hwnd; _window = Window.GetWindow(root);
        Native.EnsureLoaded();
        _renderer = Native.Create(hwnd);
        if (_renderer == 0) throw new InvalidOperationException(Native.Error(0));
        _monitor = MonitorFromWindow(hwnd, 2);
        Image.IsFrontBufferAvailableChanged += FrontBufferChanged;
        _root.IsVisibleChanged += VisibilityChanged; _root.Loaded += LayoutChanged;
        if (_window is not null) { _window.StateChanged += LayoutChanged; _window.LocationChanged += LayoutChanged; _window.SizeChanged += LayoutChanged; }
        Theme.Changed += Wake;
    }
    public void Start()
    {
        if (_disposed || _active) return;
        // A pending D3DImage lock can suppress compositor callbacks. The UI
        // dispatcher must independently poll GPU completion to release that lock.
        _active = true; _timer.Tick += Render; _timer.Start();
    }
    private bool TryLockImage()
    {
        if (_imageLocked) return true;
        if (!Image.TryLock(new Duration(TimeSpan.Zero)))
        {
            // WPF 8 LockImpl increments its lock count even on a timeout.
            // Balance it without touching the surface, allowing the pending
            // compositor present to complete instead of accumulating locks.
            Image.Unlock(); _lockMisses++; return false;
        }
        _imageLocked = true;
        return true;
    }
    private void UnlockImage()
    {
        if (!_imageLocked) return;
        _imageLocked = false;
        Image.Unlock();
    }
    private void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => Wake();
    private void LayoutChanged(object? sender, EventArgs e) => Wake();
    private void SetInterval(int milliseconds)
    {
        var next=TimeSpan.FromMilliseconds(milliseconds);
        if (_timer.Interval!=next) _timer.Interval=next;
    }
    private void Wake()
    {
        if (_disposed || !_active) return;
        if (_renderer != 0) Native.Invalidate(_renderer);
        _idleTicks = 0;
        if (!_timer.IsEnabled) _lastTick = -30;
        SetInterval(16);
        _timer.Start(); // drain pending work even when becoming hidden
    }
    private void ReleaseLease()
    {
        if (_surface != 0) { Marshal.Release(_surface); _surface = 0; }
        _width = _height = 0; _surfaceReady = false;
    }
    private bool ClearSurface()
    {
        if (!_imageLocked)
        {
            if (!Image.TryLock(new Duration(TimeSpan.FromMilliseconds(50))))
            {
                Image.Unlock(); _lockMisses++; return false;
            }
            _imageLocked = true;
        }
        try { Image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, 0); }
        finally { try { UnlockImage(); } finally { ReleaseLease(); } }
        return true;
    }
    private void StopAfterFailure(Exception error)
    {
        var failed = Failed;
        Dispose();
        failed?.Invoke(error.Message);
    }
    private void FrontBufferChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (_disposed) return;
        try
        {
            // WPF must retain its interop bitmap while the front buffer is lost;
            // detaching here would prevent the recovery notification.
            if (!Image.IsFrontBufferAvailable) { UnlockImage(); _timer.Stop(); return; }
            Wake();
            _publishSurface = true;
            if (_surface == 0 || !_surfaceReady || !TryLockImage()) return;
            Image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, _surface);
            if (Native.Pending(_renderer) == 0)
            {
                if (_width > 0 && _height > 0) Image.AddDirtyRect(new Int32Rect(0, 0, _width, _height));
                _publishSurface = false; UnlockImage();
            }
        }
        catch (Exception error) { StopAfterFailure(error); }
    }
    private void Render(object? sender, EventArgs e)
    {
        if (_disposed) return;
        _ticks++;
        try
        {
            if (Window.GetWindow(_root)?.WindowState == WindowState.Minimized || !_root.IsVisible || !_root.IsLoaded ||
                _root.ActualWidth < 1 || _root.ActualHeight < 1 || !Image.IsFrontBufferAvailable)
            {
                if (_imageLocked && Image.IsFrontBufferAvailable)
                {
                    int completed = Native.Render(_renderer, 0, 0, _width, _height, (float)GlassPalette.Density, Theme.AppsDark ? 1 : 0);
                    if (completed < 0) throw new InvalidOperationException(Native.Error(_renderer));
                    if (completed > 0)
                    {
                        _surfaceReady = true;
                        if (_publishSurface) Image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, _surface);
                        Image.AddDirtyRect(new Int32Rect(0, 0, _width, _height));
                        _publishSurface = false; Frames++;
                    }
                    if (Native.Pending(_renderer) == 0) UnlockImage();
                }
                if (!_imageLocked) _timer.Stop();
                return;
            }
            long tick = _clock.ElapsedMilliseconds;
            // Pending GPU writes keep their original lock until completion. Poll on the
            // next compositor tick; new desktop work is limited to about 30 frames/s.
            if (!_imageLocked && tick - _lastTick < 30) return;
            if (!_imageLocked) _lastTick = tick;
            var monitor = MonitorFromWindow(_hwnd, 2);
            if (monitor != _monitor)
            {
                if (!ClearSurface()) return; Native.Destroy(_renderer); _renderer = 0;
                _renderer = Native.Create(_hwnd);
                if (_renderer == 0) throw new InvalidOperationException(Native.Error(0));
                _monitor = monitor;
            }
            var dpi = VisualTreeHelper.GetDpi(_root);
            int width = Math.Max(1, (int)Math.Round(_root.ActualWidth * dpi.DpiScaleX));
            int height = Math.Max(1, (int)Math.Round(_root.ActualHeight * dpi.DpiScaleY));
            if (width != _width || height != _height)
            {
                if (!ClearSurface()) return;
                if (!TryLockImage()) return;
                if (Native.Resize(_renderer, width, height) < 0) throw new InvalidOperationException(Native.Error(_renderer));
                _surface = Native.Surface(_renderer);
                if (_surface == 0) throw new InvalidOperationException("GPU shared surface unavailable");
                _width = width; _height = height; _publishSurface = true;
            }
            if (!TryLockImage()) return;
            var point = _root.PointToScreen(new Point());
            _nativeCalls++;
            int result = Native.Render(_renderer, (int)Math.Round(point.X), (int)Math.Round(point.Y), width, height,
                (float)GlassPalette.Density, Theme.AppsDark ? 1 : 0);
            if (result < 0) throw new InvalidOperationException(Native.Error(_renderer));
            if (result > 0) _surfaceReady = true;
            if (_surfaceReady && (result > 0 || (_publishSurface && Native.Pending(_renderer) == 0)))
            {
                if (_publishSurface) Image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, _surface);
                Image.AddDirtyRect(new Int32Rect(0, 0, width, height));
                _publishSurface = false;
                if (result > 0) Frames++;
            }
            // Unlock only when no GPU write remains. A static desktop is released
            // immediately rather than holding a lock or issuing redundant draws.
            if (Native.Pending(_renderer) == 0)
            {
                UnlockImage();
                if (result > 0) _idleTicks = 0; else _idleTicks++;
                SetInterval(_idleTicks >= 3 ? 50 : 16);
            }
            else SetInterval(16);
        }
        catch (Exception error) { StopAfterFailure(error); }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_active) { _timer.Stop(); _timer.Tick -= Render; _active = false; }
        Image.IsFrontBufferAvailableChanged -= FrontBufferChanged;
        _root.IsVisibleChanged -= VisibilityChanged; _root.Loaded -= LayoutChanged;
        if (_window is not null) { _window.StateChanged -= LayoutChanged; _window.LocationChanged -= LayoutChanged; _window.SizeChanged -= LayoutChanged; }
        Theme.Changed -= Wake;
        try { if (!ClearSurface()) Log.Notice("Live glass cleanup lock exceeded 50ms; WPF retains its own surface reference until image release."); }
        catch (Exception error) { Log.Notice("Live glass surface cleanup: " + error.Message); }
        finally
        {
            ReleaseLease();
            if (_renderer != 0) { Native.Destroy(_renderer); _renderer = 0; }
        }
    }
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    private static class Native
    {
        internal static nint _library;
        private static readonly object Gate = new();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int CountCall();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate nint CreateCall(nint hwnd);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int ResizeCall(nint context, int width, int height);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int RenderCall(nint context, int x, int y, int width, int height, float density, int dark);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate nint SurfaceCall(nint context);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int PendingCall(nint context);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void DestroyCall(nint context);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint ErrorCall(nint context);
        internal static CreateCall Create = null!;
        internal static ResizeCall Resize = null!;
        internal static RenderCall Render = null!;
        internal static SurfaceCall Surface = null!;
        internal static PendingCall Pending = null!;
        internal static DestroyCall Destroy = null!;
        internal static DestroyCall Invalidate = null!;
        internal static CountCall ContextCount = null!;
        private static ErrorCall _error = null!;
        private static ErrorCall _stats = null!;
        internal static string Stats(nint context) => Marshal.PtrToStringUni(_stats(context)) ?? "no-stats";
        internal static string Error(nint context) => Marshal.PtrToStringUni(_error(context)) ?? "Native live glass failed";
        internal static void EnsureLoaded()
        {
            lock (Gate)
            {
                if (_library != 0) return;
                using var resource = typeof(LiveGlassBackdrop).Assembly.GetManifestResourceStream("Snapline.LiveGlass.dll")
                    ?? throw new FileNotFoundException("Native glass resource was not built");
                using var bytes = new MemoryStream(); resource.CopyTo(bytes); var data = bytes.ToArray();
                string hash = Convert.ToHexString(SHA256.HashData(data));
                string folder = Path.Combine(Settings.Folder, ".native", hash);
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, "Tendedero.LiveGlass.dll");
                if (!File.Exists(path))
                {
                    string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try { File.WriteAllBytes(temporary, data); File.Move(temporary, path); }
                    catch (IOException) when (File.Exists(path)) { }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
                if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) != hash)
                    throw new IOException("Native glass resource checksum mismatch");
                var library = NativeLibrary.Load(path);
                try
                {
                    T Get<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
                    Create = Get<CreateCall>("LiveGlass_Create"); Resize = Get<ResizeCall>("LiveGlass_Resize");
                    Render = Get<RenderCall>("LiveGlass_Render"); Surface = Get<SurfaceCall>("LiveGlass_Surface9");
                    Pending = Get<PendingCall>("LiveGlass_IsPending");
                    Destroy = Get<DestroyCall>("LiveGlass_Destroy");
                    Invalidate = Get<DestroyCall>("LiveGlass_Invalidate");
                    ContextCount = Get<CountCall>("LiveGlass_ContextCount");
                    _stats = Get<ErrorCall>("LiveGlass_Stats");
                    _error = Get<ErrorCall>("LiveGlass_LastError"); _library = library;
                }
                catch { NativeLibrary.Free(library); throw; }
            }
        }
    }
}