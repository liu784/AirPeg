using System.Windows;
using System.Windows.Media;
using Snapline.Core;
namespace Snapline.UI;

/// <summary>Interactive chrome only. Photo/rope brushes are deliberately separate.</summary>
public static class GlassPalette
{
    public static bool Enabled => Settings.Current.LiquidGlassEnabled && !SystemParameters.HighContrast;
    public static double Density => Settings.Current.GlassIntensity / 100;
    public static void Apply(ResourceDictionary r, bool dark)
    {
        Color C(string value) => (Color)ColorConverter.ConvertFromString(value);
        Brush B(string light, string night) => Theme.Freeze(new SolidColorBrush(C(dark ? night : light)));
        Brush Layer(string light, string night, double opacity) { var c=C(dark?night:light); c.A=(byte)Math.Round(255*opacity); return Theme.Freeze(new SolidColorBrush(c)); }
        r["ChromeText"] = B("#192330", "#F0F4F9");
        r["ChromeMuted"] = B("#475668", "#B6C1CF");
        r["ChromeBase"] = B("#EEF2F6", "#171E28");
        r["ChromePanel"] = Layer("#FFFFFF", "#293340", Enabled ? .72 + Density*.22 : 1);
        // Reading surfaces have a theme-specific readability floor independent of optical density.
        r["ChromeReadingPanel"] = Layer("#F8FAFD", "#222D3A", Enabled ? (dark ? .68 + Density*.18 : .62 + Density*.22) : 1);
        var settingsEdge=new LinearGradientBrush { StartPoint=new Point(0,0),EndPoint=new Point(1,1) };
        settingsEdge.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(dark?110:190),255,255,255),0));
        settingsEdge.GradientStops.Add(new GradientStop(Color.FromArgb(30,255,255,255),.45));
        settingsEdge.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(dark?75:130),255,255,255),1));
        r["SettingsGlassEdge"]=Theme.Freeze(settingsEdge);
        r["SettingsContentEdge"]=r["SettingsGlassEdge"];
        r["ChromeEdge"] = B("#AFBAC7", "#69788A");
        r["ChromeAccent"] = B("#245CA8", "#90BCFF");
        r["ChromeOnAccent"] = B("#FFFFFF", "#102744");
        r["ChromeHover"] = B("#E2ECF9", "#3B4A5F");
        r["ChromePressed"] = B("#CCDDF1", "#1B2C42");
        r["ChromeSelected"] = B("#D8E8FC", "#314761");
        r["ChromeTrack"] = B("#8190A2", "#8996A7");
        r["ChromeShine"] = Layer("#FFFFFF", "#C8E1FF", Enabled ? (dark?.18:.9) : 0);
        var gradient = new LinearGradientBrush(C(dark?"#465262":"#FFFFFF"), C(dark?"#283341":"#E3EAF2"), 90);
        gradient.Opacity = Enabled ? .84 + Density*.16 : 1;
        r["ChromeControl"] = Enabled ? Theme.Freeze(gradient) : B("#F7F9FC", "#2B3644");
        if (SystemParameters.HighContrast)
        {
            foreach (var key in new[]{"ChromeBase","ChromePanel","ChromeReadingPanel","ChromeControl"}) r[key]=SystemColors.WindowBrush;
            foreach (var key in new[]{"ChromeText","ChromeMuted","ChromeEdge","ChromeTrack"}) r[key]=SystemColors.WindowTextBrush;
            foreach (var key in new[]{"ChromeAccent","ChromeHover","ChromePressed","ChromeSelected"}) r[key]=SystemColors.HighlightBrush;
            r["ChromeOnAccent"]=SystemColors.HighlightTextBrush;
        }
        // Menus and tooltips use an opaque readable backing, never blur their labels.
        r["MenuBg"]=r["ChromeBase"]; r["MenuFg"]=r["ChromeText"]; r["MenuFgSecondary"]=r["ChromeMuted"];
        r["MenuHover"]=r["ChromeHover"]; r["MenuBorder"]=r["ChromeEdge"];
    }
}
