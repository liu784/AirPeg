using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Snapline.Core;
using Line = Snapline.Core.Line;

namespace Snapline.UI;

/// <summary>The line and where each photo hangs. Drives every animation from one clock.</summary>
public sealed class LineCanvas : Canvas
{
    private readonly Line _line;
    private readonly List<PeggedControl> _cards = new();
    private readonly Grid _rope;
    private readonly Path _ropeShadow, _ropeCore, _ropeHighlight;
    private PeggedControl? _held;
    private bool _ropeDrag;
    private readonly Border _hint;
    private readonly LockHanger _lockButton;
    private bool _locked;
    private bool _lockHangerEnabled;
    public event Action? LockRequested;
    public bool Locked { get => _locked; set { _locked = value && _lockHangerEnabled; UpdateLockVisual(); } }
    public bool LockHangerEnabled
    {
        get => _lockHangerEnabled;
        set
        {
            if (_lockHangerEnabled == value && _lockButton.Visibility == (value ? Visibility.Visible : Visibility.Collapsed)) return;
            _lockHangerEnabled = value;
            _lockButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            _lockButton.IsEnabled = value;
            if (!value) { _locked = false; _lockButton.SetHovering(false); }
            UpdateLockVisual(); Relayout(snap: false);
        }
    }
    public Rect? LockRect => _lockHangerEnabled
        ? new Rect(GetLeft(_lockButton), GetTop(_lockButton), LockHanger.HitWidth, LockHanger.HitHeight) : null;
    private void UpdateLockVisual() => _lockButton.Locked = _locked;
    private double CardMaxX => Math.Max(Layout.CardWidth / 2,
        _width - Layout.CardWidth / 2 - (_lockHangerEnabled ? 96 : 0));
    private double CardSlotX(int index, int count)
    {
        double x = Layout.X(index, count, _width);
        // Keep the accepted centered group whenever it fits. On a narrow screen,
        // shift the group together so the last slot does not compress into its neighbour.
        if (_lockHangerEnabled) x -= Math.Max(0, Layout.X(count - 1, count, _width) - CardMaxX);
        return Math.Clamp(x, Layout.CardWidth / 2, CardMaxX);
    }
    private readonly Tween _hintOpacity = new(0);
    private readonly TranslateTransform _reveal = new();
    private readonly Spring _revealSpring = Spring.FromResponse(0.42, 0.82, -(Layout.PanelHeight + 12));
    private readonly Tween _hideTween = new(-(Layout.PanelHeight + 12));
    private bool _revealed;
    private bool _usingSpring;
    private double _expandRate = 1;
    private double _panelVelocity; // physical DIP/s, for an interrupted transition only
    private readonly Stopwatch _clock = new();
    private double _lastTime;
    private bool _ticking;
    private double _width;
    private bool _lastAppsDark = Theme.AppsDark;

    public LineCanvas(Line line)
    {
        _line = line;
        Background = Brushes.Transparent;
        ClipToBounds = false;
        Height = Layout.PanelHeight;
        RenderTransform = _reveal;
        _reveal.Y = -(Layout.PanelHeight + 12);

        // A thin, neutral line: a mid gray core with a faint highlight and a soft
        // shadow, so it reads on light and dark backgrounds alike. It fades out at
        // both ends so it seems to come from beyond the screen.
        _ropeShadow = new Path { Stroke = Theme.Freeze(new SolidColorBrush(Theme.Gray(0, 0.22))), StrokeThickness = 1.4, Effect = new BlurEffect { Radius = 2.4 }, RenderTransform = new TranslateTransform(0, 1.2) };
        _ropeCore = new Path { StrokeThickness = 1.2 };
        _ropeHighlight = new Path { StrokeThickness = 0.4, RenderTransform = new TranslateTransform(0, -0.35) };
        PaintRope();
        _rope = new Grid { IsHitTestVisible = false };
        _rope.Children.Add(_ropeShadow);
        _rope.Children.Add(_ropeCore);
        _rope.Children.Add(_ropeHighlight);
        var mask = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        mask.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
        mask.GradientStops.Add(new GradientStop(Colors.Black, 0.08));
        mask.GradientStops.Add(new GradientStop(Colors.Black, 0.92));
        mask.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        mask.Freeze();
        _rope.OpacityMask = mask;
        Children.Add(_rope);
        SetZIndex(_rope, 0);

        _hint = new Border
        {
            Child = new TextBlock
            {
                Text = Strings.Hint,
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"), FontSize = 12, FontWeight = FontWeights.Medium,
                Foreground = Theme.Freeze(new SolidColorBrush(Theme.Secondary)),
            },
            Padding = new Thickness(12, 6, 12, 6),
            CornerRadius = new CornerRadius(14),
            Background = Theme.Freeze(new SolidColorBrush(Theme.HintFill)),
            BorderThickness = new Thickness(0.75),
            BorderBrush = Theme.Freeze(new LinearGradientBrush(Theme.GlassEdgeTop, Theme.GlassEdgeBottom, 90)),
            Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Direction = 270, Opacity = 0.14, Color = Colors.Black },
            Opacity = 0,
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = Strings.NewCaptureTip,
        };
        // The hint is a button too: click it to start a snip.
        _hint.MouseLeftButtonUp += (_, e) => { e.Handled = true; Snapline.Interop.Shell.Snip(); };
        Children.Add(_hint);
        SetZIndex(_hint, 1);

        _lockButton = new LockHanger();
        _lockButton.Click += (_, e) => { e.Handled = true; if (_lockHangerEnabled) LockRequested?.Invoke(); };
        Children.Add(_lockButton); SetZIndex(_lockButton, 3);
        LockHangerEnabled = Settings.Current.LockHangerEnabled;

        _line.ItemsChanged += Sync;
        _line.ItemUpdated += OnItemUpdated;
        _line.Gust += () => { foreach (var c in _cards) if (!c.Item.Falling) c.Breeze(); StartTicking(); };
        _line.StateChanged += () => { foreach (var c in _cards) c.StateChanged(); StartTicking(); };
        Theme.Changed += OnThemeChanged;
        Pegs.LookChanged += Repaint;
        Strings.LanguageChanged += Repaint;
        MouseLeftButtonDown += OnRopeDown;
        MouseLeftButtonUp += OnRopeUp;
        MouseRightButtonUp += (_, e) =>
        {
            var p = e.GetPosition(this);
            if (IsOverRope(p)) { e.Handled = true; MenuRequested?.Invoke(); }
        };

        LostMouseCapture += (_, _) => { if (_ropeDrag) { _ropeDrag = false; RopeDragEnded?.Invoke(); } };
        SizeChanged += (_, _) => Relayout(snap: true);
        Sync();
    }

    public IReadOnlyList<PeggedControl> Cards => _cards;

    private void PaintRope()
    {
        // Tendedero/LineView.swift: neutral core and a translucent white highlight.
        _ropeCore.Stroke = Theme.Freeze(new SolidColorBrush(Theme.Gray(0.55)));
        _ropeHighlight.Stroke = Theme.Freeze(new SolidColorBrush(Theme.Gray(1, 0.45)));
    }

    private void OnThemeChanged()
    {
        // Glass concentration and effect toggles change chrome resources only.
        // Preserve each hanging card and its animation state on those updates.
        if (_lastAppsDark == Theme.AppsDark) { UpdateLockVisual(); return; }
        _lastAppsDark = Theme.AppsDark;
        Repaint();
    }

    private void Repaint()
    {
        PaintRope();
        UpdateLockVisual();
        if (_width > 0) DrawRope();
        _hint.Background = Theme.Freeze(new SolidColorBrush(Theme.HintFill));
        ((TextBlock)_hint.Child).Foreground = Theme.Freeze(new SolidColorBrush(Theme.Secondary));
        ((TextBlock)_hint.Child).Text = Strings.Hint;
        _hint.ToolTip = Strings.NewCaptureTip;
        _hint.FlowDirection = Strings.Flow;
        // Cards are rebuilt with the new colours.
        foreach (var c in _cards.ToList()) Children.Remove(c);
        _cards.Clear();
        Sync();
    }

    public bool Revealed
    {
        get => _revealed;
        set
        {
            if (_revealed == value) return;
            _revealed = value;
            if (value)
            {
                // Tucked away, the whole line waits above the top edge and slides
                // out from under the taskbar, the way an auto-hiding taskbar does.
                _revealSpring.Value = _reveal.Y;
                _expandRate = Settings.Current.ExpandSpeed;
                _revealSpring.Velocity = _panelVelocity / _expandRate;
                _revealSpring.Target = 0;
                _usingSpring = true;
            }
            else
            {
                double target = -(Layout.PanelHeight + 12);
                double duration = 0.22 / Settings.Current.HideSpeed;
                bool interrupted = _usingSpring && !_revealSpring.Resting;
                double distance = target - _reveal.Y;
                double slope = Math.Abs(distance) > 0.001 ? _panelVelocity * duration / distance : 0;
                _hideTween.Snap(_reveal.Y);
                // Normal hides retain P1's cubic curve. A reversal carries the current
                // velocity into a Hermite segment and reaches the hidden endpoint at rest.
                _hideTween.Go(target, duration, interrupted
                    ? t => (slope - 2) * t * t * t + (3 - 2 * slope) * t * t + slope * t
                    : Ease.InCubic);
                _usingSpring = false;
            }
            StartTicking();
        }
    }

    // MARK: Items

    private void Sync()
    {
        var items = _line.Items;
        // Remove what is gone.
        foreach (var card in _cards.Where(c => !items.Contains(c.Item)).ToList())
        {
            _cards.Remove(card);
            Children.Remove(card);
        }
        // Add what is new, in order.
        var known = _cards.Select(c => c.Item).ToHashSet();
        foreach (var item in items)
        {
            if (known.Contains(item)) continue;
            var card = new PeggedControl(item, _line);
            _cards.Add(card);
            Children.Add(card);
            SetZIndex(card, 2);
        }
        _cards.Sort((a, b) => items.ToList().IndexOf(a.Item).CompareTo(items.ToList().IndexOf(b.Item)));
        Relayout(snap: false);
        bool empty = _line.LiveCount == 0;
        _hintOpacity.Go(empty ? 1 : 0, 0.3);
        StartTicking();
    }

    private void OnItemUpdated(Pegged item)
    {
        var card = _cards.FirstOrDefault(c => c.Item == item);
        if (card is null) return;
        if (!item.Flying && card.Opacity == 0 && !item.Falling) card.Land();
        card.Refresh();
        StartTicking();
    }

    private void Relayout(bool snap)
    {
        _width = ActualWidth;
        if (_width <= 0) return;
        DrawRope();
        // Falling cards keep their slot until they are removed, so the others do not jump early.
        var live = _cards.Where(c => !c.Item.Falling).ToList();
        int count = live.Count;
        for (int i = 0; i < count; i++)
        {
            var card = live[i];
            card.RopeYAt ??= x => Layout.RopeY(x, _width);
            double x = CardSlotX(i, count);
            double ropeY = Layout.RopeY(x, _width);
            bool fresh = double.IsNaN(GetTop(card));
            SetTop(card, ropeY - Layout.PinAbove);
            card.Height = Layout.PanelHeight - ropeY;
            if (card != _held) card.SetTargetX(x, snap || fresh);
        }
        double lockX = LockHanger.CenterX(_width);
        SetLeft(_lockButton, lockX - LockHanger.HitWidth / 2); SetTop(_lockButton, Layout.RopeY(lockX, _width) - Layout.PinAbove);
        double mid = _width / 2;
        _hint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        SetLeft(_hint, mid - _hint.DesiredSize.Width / 2);
        SetTop(_hint, Layout.RopeY(mid, _width) + 34 - _hint.DesiredSize.Height / 2);
    }

    private void DrawRope()
    {
        double top = Layout.RopeTop, w = _width;
        var fig = new PathFigure { StartPoint = new Point(-20, top) };
        fig.Segments.Add(new QuadraticBezierSegment(new Point(w / 2, top + 2 * Layout.Sag(w)), new Point(w + 20, top), true));
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        geo.Freeze();
        _ropeShadow.Data = _ropeCore.Data = _ropeHighlight.Data = geo;
        _rope.Width = w;
        _rope.Height = Layout.PanelHeight;
    }

    // MARK: Reordering by hand

    public void BeginReorder(PeggedControl card)
    {
        _held = card;
        SetZIndex(card, 5);
        card.SetHovering(true);
        StartTicking();
    }

    /// <summary>The held card follows the pointer; the others make room.</summary>
    public void ReorderTo(PeggedControl card, double centerX)
    {
        if (_held != card) return;
        double half = Layout.CardWidth / 2;
        centerX = Math.Clamp(centerX, half, CardMaxX);
        card.SetTargetX(centerX, snap: true);
        var live = _cards.Where(c => !c.Item.Falling).ToList();
        int from = live.IndexOf(card);
        if (from < 0) return;
        // Which slot is the pointer over now? The nearest one.
        int to = 0;
        double best = double.MaxValue;
        for (int i = 0; i < live.Count; i++)
        {
            double d = Math.Abs(centerX - CardSlotX(i, live.Count));
            if (d < best) { best = d; to = i; }
        }
        if (to != from) _line.Move(card.Item.Id, to);
        StartTicking();
    }

    public void EndReorder(PeggedControl card)
    {
        if (_held != card) return;
        _held = null;
        SetZIndex(card, 2);
        Relayout(snap: false);
        StartTicking();
    }

    // MARK: Moving the line itself

    /// <summary>The menu was asked for, from the tag or a right click on the rope.</summary>
    public event Action? MenuRequested;

    // Kept for the upstream demo entry point; the reference has no hanging menu tag.
    public Rect? TagRect => null;

    public event Action? RopeDragStarted;
    public event Action? RopeDragEnded;
    public bool RopeDragging => _ropeDrag;

    /// <summary>Whether a canvas point lies on the rope, within a comfortable band, and not over a photo.</summary>
    public bool IsOverRope(Point p)
    {
        if (_width <= 0 || p.X < 0 || p.X > _width) return false;
        if (LockRect is { } lockRect && lockRect.Contains(p)) return false;
        if (CardHitRects().Any(h => h.rect.Contains(p))) return false;
        return Math.Abs(p.Y - Layout.RopeY(p.X, _width)) <= 9;
    }

    private void OnRopeDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!IsOverRope(e.GetPosition(this))) return;
        e.Handled = true;
        _ropeDrag = true;
        CaptureMouse();
        RopeDragStarted?.Invoke();
    }

    private void OnRopeUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_ropeDrag) return;
        e.Handled = true;
        _ropeDrag = false;
        ReleaseMouseCapture();
        RopeDragEnded?.Invoke();
    }

    /// <summary>Where the pointer counts as being over a photo, in canvas coordinates.</summary>
    public IEnumerable<(PeggedControl card, Rect rect)> HitRects() => CardHitRects();

    /// <summary>The hint counts as well while the line is empty, so it can be clicked.</summary>
    public Rect? HintRect => _line.LiveCount == 0 && _hint.Opacity > 0.5
        ? new Rect(GetLeft(_hint), GetTop(_hint), _hint.ActualWidth, _hint.ActualHeight)
        : null;

    private IEnumerable<(PeggedControl card, Rect rect)> CardHitRects()
    {
        foreach (var c in _cards)
        {
            if (c.Item.Falling || c.Item.Flying) continue;
            var r = c.CardRect;
            r.Inflate(4, 4);
            yield return (c, r);
        }
    }

    public void UpdateHover(Point? pointInCanvas)
    {
        _lockButton.SetHovering(pointInCanvas is { } hover && LockRect is { } hoverRect && hoverRect.Contains(hover));
        PeggedControl? over = null;
        if (pointInCanvas is { } p)
            foreach (var (card, rect) in CardHitRects())
                if (rect.Contains(p)) { over = card; break; }
        bool any = false;
        foreach (var c in _cards)
        {
            bool on = c == over && _line.DraggingId is null;
            if (c.Hovering != on) { c.SetHovering(on); any = true; }
        }
        if (any) StartTicking();
        Cursor = pointInCanvas is { } q && over is null && IsOverRope(q) ? System.Windows.Input.Cursors.SizeNS : null;
    }

    /// <summary>Lays the cards out again right now, snapping them into place. For offscreen rendering.</summary>
    public void RelayoutNow() => Relayout(snap: true);

    /// <summary>Steps every animation forward without a window, for offscreen rendering.</summary>
    public void Advance(double seconds, double step = 1.0 / 60)
    {
        _ticking = false;
        CompositionTarget.Rendering -= OnRendering;
        for (double t = 0; t < seconds; t += step) StepAll(step);
    }

    private bool StepAll(double dt)
    {
        bool active = false;
        if (_usingSpring)
        {
            _revealSpring.Step(dt * _expandRate);
            _reveal.Y = _revealSpring.Value;
            _panelVelocity = _revealSpring.Resting ? 0 : _revealSpring.Velocity * _expandRate;
            active |= !_revealSpring.Resting;
        }
        else
        {
            double previous = _reveal.Y;
            _hideTween.Step(dt);
            _reveal.Y = _hideTween.Value;
            _panelVelocity = _hideTween.Done || dt <= 0 ? 0 : (_reveal.Y - previous) / dt;
            active |= !_hideTween.Done;
        }
        _hintOpacity.Step(dt);
        _hint.Opacity = _hintOpacity.Value;
        active |= !_hintOpacity.Done;
        foreach (var card in _cards) { card.Tick(dt); active |= card.Active; }
        return active;
    }

    // MARK: The clock

    private void StartTicking()
    {
        if (_ticking) return;
        _ticking = true;
        _clock.Restart();
        _lastTime = 0;
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        double now = _clock.Elapsed.TotalSeconds;
        double dt = Math.Clamp(now - _lastTime, 0, 1.0 / 20);
        _lastTime = now;
        if (dt <= 0) return;

        if (!StepAll(dt))
        {
            _ticking = false;
            CompositionTarget.Rendering -= OnRendering;
        }
    }
}
