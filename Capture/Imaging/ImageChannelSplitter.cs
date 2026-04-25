// author: eng-fe-desktop
// phase: engineering
// preserve: BT.601 계수·12채널 열거·8bpp indexed 출력 원본과 동일 (namespace 변경만) — migration-mapping.md §10-2
// followup 1차: ExtractChannel GetPixel·SetPixel 루프 → LockBitmap + byte[] 직접 접근 (ADR-202)
//               ComputeHsvH·ComputeHsvS·ComputeHlsH·ComputeHlsL·ComputeHlsS private static 헬퍼 추가

using System.Drawing;
using System.Drawing.Imaging;

namespace Capture.Imaging;

public class ImageChannelSplitter
{
    public enum ImageChannels
    {
        RGB_R,
        RGB_G,
        RGB_B,
        YUV_Y,
        YUV_U,
        YUV_V,
        HSV_H,
        HSV_S,
        HSV_V,
        HLS_H,
        HLS_L,
        HLS_S
    }

    public static Bitmap ExtractChannel(Bitmap source, ImageChannels channelTo)
    {
        var lockSrc = new LockBitmap(source);
        var dst = new Bitmap(source.Width, source.Height, PixelFormat.Format8bppIndexed);
        ColorPalette palette = dst.Palette;
        for (int i = 0; i < palette.Entries.Length; i++)
            palette.Entries[i] = Color.FromArgb(i, i, i);
        dst.Palette = palette;
        var lockDst = new LockBitmap(dst);

        lockSrc.LockBits();
        lockDst.LockBits();
        try
        {
            int bppSrc = lockSrc.Depth / 8;       // 3 (24bpp) or 4 (32bpp)
            int strideSrc = lockSrc.Stride;
            int strideDst = lockDst.Stride;        // 8bpp Indexed: 1 byte per pixel + row padding
            byte[] srcPixels = lockSrc.Pixels;
            byte[] dstPixels = lockDst.Pixels;
            int w = lockSrc.Width;
            int h = lockSrc.Height;

            for (int y = 0; y < h; y++)
            {
                int rowSrc = y * strideSrc;
                int rowDst = y * strideDst;
                for (int x = 0; x < w; x++)
                {
                    int idx = rowSrc + x * bppSrc;
                    byte b = srcPixels[idx + 0];
                    byte g = srcPixels[idx + 1];
                    byte r = srcPixels[idx + 2];

                    byte outVal = channelTo switch
                    {
                        ImageChannels.RGB_R => r,
                        ImageChannels.RGB_G => g,
                        ImageChannels.RGB_B => b,
                        ImageChannels.YUV_Y => (byte)(((66 * r + 129 * g + 25 * b + 128) >> 8) + 16),
                        ImageChannels.YUV_U => (byte)(((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128),
                        ImageChannels.YUV_V => (byte)(((112 * r - 94 * g - 18 * b + 128) >> 8) + 128),
                        ImageChannels.HSV_H => ComputeHsvH(r, g, b),
                        ImageChannels.HSV_S => ComputeHsvS(r, g, b),
                        ImageChannels.HSV_V => (byte)Math.Max(r, Math.Max(g, b)),
                        ImageChannels.HLS_H => ComputeHlsH(r, g, b),
                        ImageChannels.HLS_L => ComputeHlsL(r, g, b),
                        ImageChannels.HLS_S => ComputeHlsS(r, g, b),
                        _ => 0
                    };
                    dstPixels[rowDst + x] = outVal;
                }
            }
        }
        finally
        {
            lockSrc.UnlockBits();
            lockDst.UnlockBits();
        }
        return dst;
    }

    // ── Private static 헬퍼 — HSV / HLS 채널 계산 (ADR-202) ──────────────────
    // 기존 GetPixel 루프 내부 float 분기를 byte 반환 메서드로 추출.
    // 원본 동작(조건 가드·계산식)을 그대로 보존한다.

    /// <summary>
    /// HSV Hue 채널 (0~255 스케일). cmax == 0 또는 delta == 0 이면 0 반환 (원본 동작 보존).
    /// </summary>
    private static byte ComputeHsvH(byte r, byte g, byte b)
    {
        int cmax = Math.Max(r, Math.Max(g, b));
        int cmin = Math.Min(r, Math.Min(g, b));
        float delta = cmax - cmin;
        if (cmax == 0 || delta == 0f) return 0;

        float hue;
        if (cmax == r)      hue = (float)(g - b) / delta;
        else if (cmax == g) hue = 2f + (float)(b - r) / delta;
        else                hue = 4f + (float)(r - g) / delta;
        hue *= 60f;
        if (hue < 0f) hue += 360f;
        int h = (int)Math.Round(hue * 255f / 360f);
        return (byte)Math.Clamp(h, 0, 255);
    }

    /// <summary>
    /// HSV Saturation 채널 (0~255 스케일). cmax == 0 또는 delta == 0 이면 0 반환 (원본 동작 보존).
    /// </summary>
    private static byte ComputeHsvS(byte r, byte g, byte b)
    {
        int cmax = Math.Max(r, Math.Max(g, b));
        int cmin = Math.Min(r, Math.Min(g, b));
        float delta = cmax - cmin;
        if (cmax == 0 || delta == 0f) return 0;
        int s = (int)Math.Round(delta * 255f / (float)cmax);
        return (byte)Math.Clamp(s, 0, 255);
    }

    /// <summary>
    /// HLS Hue 채널 (0~255 스케일). delta == 0 이면 0 반환 (원본 동작 보존).
    /// </summary>
    private static byte ComputeHlsH(byte r, byte g, byte b)
    {
        float rf = r / 255f;
        float gf = g / 255f;
        float bf = b / 255f;
        float maxC = Math.Max(rf, Math.Max(gf, bf));
        float minC = Math.Min(rf, Math.Min(gf, bf));
        float delta = maxC - minC;
        if (delta == 0f) return 0;

        float hue;
        if (maxC == rf)      hue = (gf - bf) / 6f / delta;
        else if (maxC == gf) hue = 1f / 3f + (bf - rf) / 6f / delta;
        else                 hue = 2f / 3f + (rf - gf) / 6f / delta;
        if (hue < 0f) hue += 1f;
        if (hue > 1f) hue -= 1f;
        int h = (int)Math.Round(hue * 255f);
        return (byte)Math.Clamp(h, 0, 255);
    }

    /// <summary>
    /// HLS Lightness 채널 (0~255 스케일).
    /// </summary>
    private static byte ComputeHlsL(byte r, byte g, byte b)
    {
        float rf = r / 255f;
        float gf = g / 255f;
        float bf = b / 255f;
        float maxC = Math.Max(rf, Math.Max(gf, bf));
        float minC = Math.Min(rf, Math.Min(gf, bf));
        int l = (int)Math.Round((maxC + minC) * 255f / 2f);
        return (byte)Math.Clamp(l, 0, 255);
    }

    /// <summary>
    /// HLS Saturation 채널 (0~255 스케일). delta == 0 이면 0 반환 (원본 동작 보존).
    /// </summary>
    private static byte ComputeHlsS(byte r, byte g, byte b)
    {
        float rf = r / 255f;
        float gf = g / 255f;
        float bf = b / 255f;
        float maxC = Math.Max(rf, Math.Max(gf, bf));
        float minC = Math.Min(rf, Math.Min(gf, bf));
        float delta = maxC - minC;
        if (delta == 0f) return 0;
        int s = ((maxC + minC) / 2f <= 0.5f)
            ? (int)Math.Round(delta * 255f / (maxC + minC))
            : (int)Math.Round(delta * 255f / (2f - maxC - minC));
        return (byte)Math.Clamp(s, 0, 255);
    }
}
