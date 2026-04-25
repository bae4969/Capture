// author: eng-fe-desktop
// phase: engineering
// preserve: 원본 로직 동일 (namespace 변경만) — migration-mapping.md §10-1

using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Capture.Imaging;

public class LockBitmap
{
    private Bitmap Source;

    private IntPtr Iptr = IntPtr.Zero;

    private BitmapData bitmapData = null!;

    public byte[] Pixels { get; set; } = Array.Empty<byte>();

    public int Depth { get; private set; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public int Stride { get; private set; }

    public LockBitmap(Bitmap source)
    {
        Source = source;
    }

    public void LockBits()
    {
        Width = Source.Width;
        Height = Source.Height;
        Rectangle rect = new Rectangle(0, 0, Width, Height);
        Depth = Image.GetPixelFormatSize(Source.PixelFormat);
        if (Depth != 8 && Depth != 24 && Depth != 32)
        {
            throw new ArgumentException("Unsupported pixel format. Must be 8, 24, or 32 bpp.");
        }
        bitmapData = Source.LockBits(rect, ImageLockMode.ReadWrite, Source.PixelFormat);
        Stride = bitmapData.Stride;
        int num = Stride * Height;
        Pixels = new byte[num];
        Iptr = bitmapData.Scan0;
        Marshal.Copy(Iptr, Pixels, 0, Pixels.Length);
    }

    public void UnlockBits()
    {
        Marshal.Copy(Pixels, 0, Iptr, Pixels.Length);
        Source.UnlockBits(bitmapData);
    }

    public Color GetPixel(int x, int y)
    {
        Color result = Color.Empty;
        int num = Depth / 8;
        int num2 = y * Stride + x * num;
        if (Depth == 32)
        {
            byte blue = Pixels[num2];
            byte green = Pixels[num2 + 1];
            byte red = Pixels[num2 + 2];
            result = Color.FromArgb(Pixels[num2 + 3], red, green, blue);
        }
        if (Depth == 24)
        {
            byte blue2 = Pixels[num2];
            byte green2 = Pixels[num2 + 1];
            result = Color.FromArgb(Pixels[num2 + 2], green2, blue2);
        }
        if (Depth == 8)
        {
            byte b = Pixels[num2];
            result = Color.FromArgb(b, b, b);
        }
        return result;
    }

    public void SetPixel(int x, int y, Color color)
    {
        int num = Depth / 8;
        int num2 = (y * Stride + x) * num;
        if (Depth == 32)
        {
            Pixels[num2] = color.B;
            Pixels[num2 + 1] = color.G;
            Pixels[num2 + 2] = color.R;
            Pixels[num2 + 3] = color.A;
        }
        if (Depth == 24)
        {
            Pixels[num2] = color.B;
            Pixels[num2 + 1] = color.G;
            Pixels[num2 + 2] = color.R;
        }
        if (Depth == 8)
        {
            Pixels[num2] = color.B;
        }
    }
}
