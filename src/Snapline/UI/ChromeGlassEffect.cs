using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
namespace Snapline.UI;
public sealed class ChromeGlassEffect : ShaderEffect
{
    private static readonly PixelShader Shader = new() { UriSource = new Uri("pack://application:,,,/Snapline;component/Assets/Shaders/ChromeGlass.ps"), ShaderRenderMode = ShaderRenderMode.HardwareOnly };
    public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty(nameof(Input), typeof(ChromeGlassEffect), 0);
    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(nameof(Size), typeof(Point), typeof(ChromeGlassEffect), new UIPropertyMetadata(new Point(600,700), PixelShaderConstantCallback(0)));
    public static readonly DependencyProperty DensityProperty = DependencyProperty.Register(nameof(Density), typeof(double), typeof(ChromeGlassEffect), new UIPropertyMetadata(.5, PixelShaderConstantCallback(1)));
    public static readonly DependencyProperty DarkProperty = DependencyProperty.Register(nameof(Dark), typeof(double), typeof(ChromeGlassEffect), new UIPropertyMetadata(0d, PixelShaderConstantCallback(2)));
    public static readonly DependencyProperty TextSafeProperty = DependencyProperty.Register(nameof(TextSafe), typeof(double), typeof(ChromeGlassEffect), new UIPropertyMetadata(0d, PixelShaderConstantCallback(3)));
    public Brush Input { get => (Brush)GetValue(InputProperty); set => SetValue(InputProperty,value); }
    public Point Size { get => (Point)GetValue(SizeProperty); set => SetValue(SizeProperty,value); }
    public double Density { get => (double)GetValue(DensityProperty); set => SetValue(DensityProperty,value); }
    public double Dark { get => (double)GetValue(DarkProperty); set => SetValue(DarkProperty,value); }
    public double TextSafe { get => (double)GetValue(TextSafeProperty); set => SetValue(TextSafeProperty,value); }
    public ChromeGlassEffect() { PixelShader=Shader; UpdateShaderValue(InputProperty); UpdateShaderValue(SizeProperty); UpdateShaderValue(DensityProperty); UpdateShaderValue(DarkProperty); UpdateShaderValue(TextSafeProperty); }
}
