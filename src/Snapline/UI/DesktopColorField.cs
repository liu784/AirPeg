using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Snapline.UI;

/// <summary>Retains broad area averages, never individual desktop pixels.</summary>
public static class DesktopColorField
{
    public static BitmapSource Create(System.Drawing.Bitmap bitmap, int columns = 3, int rows = 2)
    {
        if (columns is < 1 or > 9) throw new ArgumentOutOfRangeException(nameof(columns));
        if (rows is < 1 or > 6) throw new ArgumentOutOfRangeException(nameof(rows));
        int width=Math.Min(columns,bitmap.Width), height=Math.Min(rows,bitmap.Height);
        var sums=new long[width*height*3]; var counts=new long[width*height];
        var column=new int[bitmap.Width];
        for(int x=0;x<column.Length;x++) column[x]=x*width/bitmap.Width;
        var area=new System.Drawing.Rectangle(0,0,bitmap.Width,bitmap.Height);
        var data=bitmap.LockBits(area,System.Drawing.Imaging.ImageLockMode.ReadOnly,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var row=new byte[bitmap.Width*4];
            for(int y=0;y<bitmap.Height;y++)
            {
                Marshal.Copy(data.Scan0+y*data.Stride,row,0,row.Length);
                int line=y*height/bitmap.Height*width;
                for(int x=0;x<bitmap.Width;x++)
                {
                    int cell=line+column[x];
                    counts[cell]++;
                    int offset=x*4, target=cell*3;
                    sums[target]+=row[offset]; sums[target+1]+=row[offset+1]; sums[target+2]+=row[offset+2];
                }
            }
        }
        finally { bitmap.UnlockBits(data); }
        var pixels=new byte[width*height*4];
        for(int i=0;i<counts.Length;i++)
        {
            for(int c=0;c<3;c++)pixels[i*4+c]=(byte)Math.Round((double)sums[i*3+c]/counts[i]);
            pixels[i*4+3]=255;
        }
        var source=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4);
        source.Freeze(); return source;
    }
}
