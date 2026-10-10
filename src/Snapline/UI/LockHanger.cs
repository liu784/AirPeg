using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Snapline.UI;

/// <summary>A colorless glass lock held by the exact clothespin used on screenshot cards.</summary>
public sealed class LockHanger : Button
{
    public const double DecorationWidth = 26;
    public const double DecorationHeight = 30;
    public const double HitWidth = 44;
    public const double HitHeight = 48;
    public const double RightMargin = 32;
    private static readonly Geometry ClosedLock = Icon("M 3,6 V 4 A 3,3 0 0 1 9,4 V 6 M 1,6 H 11 V 14 H 1 Z M 6,9 V 11");
    // Lift the shackle outward: the open state must read at the actual 13 x 14 DIP size.
    private static readonly Geometry OpenLock = Icon("M 6,6 V 4 A 3,3 0 0 1 12,4 V 4.5 M 1,6 H 11 V 14 H 1 Z M 6,9 V 11");
    private readonly Border _body, _reflection;
    private readonly Path _icon, _iconHalo;
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly DropShadowEffect _shadow = new() { BlurRadius = 5.5, ShadowDepth = 1, Direction = 270, Opacity = .08, Color = Colors.Black };
    private bool _locked, _hovering, _pressed;
    public bool Locked { get => _locked; set { _locked = value; RefreshMaterial(); } }

    public LockHanger()
    {
        Width = HitWidth; Height = HitHeight; Focusable = false; Cursor = Cursors.Hand;
        var hit = new FrameworkElementFactory(typeof(Border));
        // Nonzero alpha is required by layered-window hit testing, even away from the ornament.
        hit.SetValue(Border.BackgroundProperty, Theme.Freeze(new SolidColorBrush(Color.FromArgb(2, 0, 0, 0))));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        hit.AppendChild(content);
        Template = new ControlTemplate(typeof(Button)) { VisualTree = hit };
        var scene = new Canvas { Width = HitWidth, Height = HitHeight, IsHitTestVisible = false };
        _body = new Border { Width = DecorationWidth, Height = DecorationHeight, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(.8),
            RenderTransform = _scale, RenderTransformOrigin = new Point(.5, 0) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(_body, "LockOrnament");
        var face = new Grid();
        _reflection = new Border { CornerRadius = new CornerRadius(7.2), BorderThickness = new Thickness(.7), Margin = new Thickness(.35),
            BorderBrush = Theme.Freeze(new LinearGradientBrush(Theme.Gray(1, .58), Theme.Gray(1, 0), 90)) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(_reflection, "LockReflection");
        face.Children.Add(_reflection);
        var iconHost = new Canvas();
        // Sharing the geometry aligns both strokes. The white rim behind the dark stroke
        // keeps this small glyph readable over light, dark and colored backgrounds.
        _iconHalo = Glyph(2.1, Theme.Gray(1, .84));
        _icon = Glyph(1.15, Theme.Gray(.13, .94));
        System.Windows.Automation.AutomationProperties.SetAutomationId(_iconHalo, "LockGlyphHalo");
        System.Windows.Automation.AutomationProperties.SetAutomationId(_icon, "LockGlyph");
        foreach (var glyph in new[] { _iconHalo, _icon })
        {
            Canvas.SetLeft(glyph, (DecorationWidth - 1.6 - 13) / 2); Canvas.SetTop(glyph, 11.4);
            iconHost.Children.Add(glyph);
        }
        face.Children.Add(iconHost); _body.Child = face;
        Canvas.SetLeft(_body, (HitWidth - DecorationWidth) / 2); Canvas.SetTop(_body, Layout.CardOffsetBelowTop);
        scene.Children.Add(_body);
        // Do not recreate, scale, recolor or put glass above the metal. This is the exact card factory.
        var peg = Pegs.Create();
        Canvas.SetLeft(peg, (HitWidth - Layout.PinWidth) / 2); Canvas.SetTop(peg, 0);
        System.Windows.Automation.AutomationProperties.SetAutomationId(peg, "LockHangerPeg");
        scene.Children.Add(peg); Canvas.SetZIndex(peg, 1);
        Content = scene;
        System.Windows.Automation.AutomationProperties.SetAutomationId(this, "LockExpanded");
        MouseEnter += (_, _) => SetHovering(true);
        MouseLeave += (_, _) => { _pressed = false; SetHovering(false); };
        PreviewMouseLeftButtonDown += (_, _) => { _pressed = true; RefreshMotion(); };
        PreviewMouseLeftButtonUp += (_, _) => { _pressed = false; RefreshMotion(); };
        LostMouseCapture += (_, _) => { _pressed = false; RefreshMotion(); };
        RefreshMaterial(animate: false);
    }

    private static Path Glyph(double thickness, Color color) => new() { Width = 13, Height = 14, StrokeThickness = thickness, Stretch = Stretch.None,
        Stroke = Theme.Freeze(new SolidColorBrush(color)), StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
    private static Geometry Icon(string data)
    {
        var g = Geometry.Parse(data).Clone();
        g.Transform = new MatrixTransform(.86, 0, 0, .86, .6, .5);
        g.Freeze(); return g;
    }
    public static double CenterX(double width) => Math.Max(HitWidth / 2, width - RightMargin - DecorationWidth / 2);
    public void SetHovering(bool on)
    {
        on &= IsEnabled;
        if (_hovering == on) return;
        _hovering = on; RefreshMaterial();
    }
    public void RefreshMaterial(bool animate = true)
    {
        ToolTip = _locked ? Strings.UnlockExpanded : Strings.LockExpanded;
        System.Windows.Automation.AutomationProperties.SetName(this, (string)ToolTip);
        var geometry = _locked ? ClosedLock : OpenLock;
        bool changingState = _icon.Data is not null && _icon.Data != geometry;
        foreach (var glyph in new[] { _iconHalo, _icon })
        {
            glyph.Data = geometry;
            if (!animate || !SystemParameters.ClientAreaAnimation || changingState)
            {
                glyph.BeginAnimation(UIElement.OpacityProperty, null); glyph.Opacity = 1;
                if (changingState && animate && SystemParameters.ClientAreaAnimation)
                    glyph.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(.7, 1, TimeSpan.FromMilliseconds(150)) { FillBehavior = FillBehavior.Stop });
            }
        }
        // State is conveyed only by geometry: the clear material and settled glyph opacity stay identical.
        if (GlassPalette.Enabled)
        {
            double density = Math.Clamp(GlassPalette.Density, 0, 1);
            var glass = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            glass.GradientStops.Add(new GradientStop(Theme.Gray(1, .08 + density * .04), 0));
            glass.GradientStops.Add(new GradientStop(Theme.Gray(1, .02 + density * .02), .48));
            glass.GradientStops.Add(new GradientStop(Theme.Gray(1, .05 + density * .03), 1));
            _body.Background = Theme.Freeze(glass);
            _body.BorderBrush = Theme.Freeze(new SolidColorBrush(Theme.Gray(1, .72)));
            _body.Effect = _shadow; _reflection.Visibility = Visibility.Visible;
        }
        else
        {
            _body.Background = Theme.Freeze(new SolidColorBrush(Theme.Gray(1, .10)));
            _body.BorderBrush = Theme.Freeze(new SolidColorBrush(Theme.Gray(1, .58)));
            _body.Effect = null; _reflection.Visibility = Visibility.Collapsed;
        }
        RefreshMotion(animate);
    }
    private void RefreshMotion(bool animate = true)
    {
        double scale = _pressed ? .96 : _hovering ? 1.035 : 1;
        foreach (var property in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
        {
            double old = (double)_scale.GetValue(property);
            _scale.BeginAnimation(property, null); _scale.SetValue(property, scale);
            if (animate && SystemParameters.ClientAreaAnimation)
                _scale.BeginAnimation(property, new DoubleAnimation(old, scale, TimeSpan.FromMilliseconds(150)) { FillBehavior = FillBehavior.Stop, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        _shadow.Opacity = _pressed ? .05 : _hovering ? .11 : .08;
        double shine = _pressed ? .40 : _hovering ? .75 : .58;
        double previous = _reflection.Opacity;
        _reflection.BeginAnimation(UIElement.OpacityProperty, null); _reflection.Opacity = shine;
        if (animate && SystemParameters.ClientAreaAnimation)
            _reflection.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(previous, shine, TimeSpan.FromMilliseconds(150)) { FillBehavior = FillBehavior.Stop });
    }
}
