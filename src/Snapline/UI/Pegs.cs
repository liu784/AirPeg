using System.Windows.Controls;

namespace Snapline.UI;

/// <summary>Tendedero's aluminium clothespin. Visual variants are not part of the reference.</summary>
public static class Pegs
{
    public static event Action? LookChanged;
    public static void RaiseLookChanged() => LookChanged?.Invoke();
    public static Grid Create() => Glass.Clothespin();
}
