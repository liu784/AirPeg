using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace Snapline.UI;
/// <summary>App-owned notices; safe cancel default on destructive history actions.</summary>
public static class GlassDialog
{
    public static bool Show(Window? owner, string text, string title, bool confirm=false)
    {
        var window=new Window { Title=title, Width=460, SizeToContent=SizeToContent.Height, ResizeMode=ResizeMode.NoResize,
            WindowStartupLocation=owner is null?WindowStartupLocation.CenterScreen:WindowStartupLocation.CenterOwner, FontSize=14, ShowInTaskbar=false, Icon=Brand.Icon };
        if(owner is { IsVisible:true }) window.Owner=owner;
        var root=new StackPanel { Margin=new Thickness(24) };
        root.Children.Add(new TextBlock { Text=text,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,20) });
        var actions=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right };
        var cancel=new Button { Content=confirm?Strings.CancelAction:Strings.Done,MinWidth=84,IsCancel=true,IsDefault=true };
        cancel.Click+=(_,_)=>window.DialogResult=false;
        if(confirm)
        {
            var yes=new Button { Content=Strings.ConfirmAction,MinWidth=84,Margin=new Thickness(0,0,10,0) };
            yes.Click+=(_,_)=>window.DialogResult=true; actions.Children.Add(yes);
        }
        actions.Children.Add(cancel); root.Children.Add(actions); GlassWindow.Attach(window,root);
        return window.ShowDialog()==true;
    }
}
