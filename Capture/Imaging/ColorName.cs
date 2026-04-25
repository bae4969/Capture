// author: eng-fe-desktop
// phase: engineering
// preserve: ~148 CSS 색명 LUT 원본과 동일 (namespace 변경만) — migration-mapping.md §10-3

using System.Drawing;

namespace Capture.Imaging;

public class ColorName
{
    private static readonly Dictionary<Color, string> LUT_COLOR = new();

    public static string GetColorName(Color color)
    {
        InitializeLUT();
        if (LUT_COLOR.TryGetValue(color, out string? name))
            return name;
        return string.Empty;
    }

    private static void InitializeLUT()
    {
        if (LUT_COLOR.Count > 0) return;

        LUT_COLOR.Add(Color.FromArgb(255, 192, 203), "Pink");
        LUT_COLOR.Add(Color.FromArgb(255, 182, 193), "LightPink");
        LUT_COLOR.Add(Color.FromArgb(255, 105, 180), "HotPink");
        LUT_COLOR.Add(Color.FromArgb(255, 20, 147), "DeepPink");
        LUT_COLOR.Add(Color.FromArgb(219, 112, 147), "PaleVioletRed");
        LUT_COLOR.Add(Color.FromArgb(199, 21, 133), "MediumVioletRed");
        LUT_COLOR.Add(Color.FromArgb(255, 160, 122), "LightSalmon");
        LUT_COLOR.Add(Color.FromArgb(250, 128, 114), "Salmon");
        LUT_COLOR.Add(Color.FromArgb(233, 150, 122), "DarkSalmon");
        LUT_COLOR.Add(Color.FromArgb(240, 128, 128), "LightCoral");
        LUT_COLOR.Add(Color.FromArgb(205, 92, 92), "IndianRed");
        LUT_COLOR.Add(Color.FromArgb(220, 20, 60), "Crimson");
        LUT_COLOR.Add(Color.FromArgb(178, 34, 34), "FireBrick");
        LUT_COLOR.Add(Color.FromArgb(139, 0, 0), "DarkRed");
        LUT_COLOR.Add(Color.FromArgb(255, 0, 0), "Red");
        LUT_COLOR.Add(Color.FromArgb(255, 69, 0), "OrangeRed");
        LUT_COLOR.Add(Color.FromArgb(255, 99, 71), "Tomato");
        LUT_COLOR.Add(Color.FromArgb(255, 127, 80), "Coral");
        LUT_COLOR.Add(Color.FromArgb(255, 140, 0), "DarkOrange");
        LUT_COLOR.Add(Color.FromArgb(255, 165, 0), "Orange");
        LUT_COLOR.Add(Color.FromArgb(255, 255, 0), "Yellow");
        LUT_COLOR.Add(Color.FromArgb(255, 255, 224), "LightYellow");
        LUT_COLOR.Add(Color.FromArgb(255, 250, 205), "LemonChiffon");
        LUT_COLOR.Add(Color.FromArgb(250, 250, 210), "LightGoldenrodYellow");
        LUT_COLOR.Add(Color.FromArgb(255, 239, 213), "PapayaWhip");
        LUT_COLOR.Add(Color.FromArgb(255, 228, 181), "Moccasin");
        LUT_COLOR.Add(Color.FromArgb(255, 218, 185), "PeachPuff");
        LUT_COLOR.Add(Color.FromArgb(238, 232, 170), "PaleGoldenrod");
        LUT_COLOR.Add(Color.FromArgb(240, 230, 140), "Khaki");
        LUT_COLOR.Add(Color.FromArgb(189, 183, 107), "DarkKhaki");
        LUT_COLOR.Add(Color.FromArgb(255, 215, 0), "Gold");
        LUT_COLOR.Add(Color.FromArgb(255, 248, 220), "Cornsilk");
        LUT_COLOR.Add(Color.FromArgb(255, 235, 205), "BlanchedAlmond");
        LUT_COLOR.Add(Color.FromArgb(255, 228, 196), "Bisque");
        LUT_COLOR.Add(Color.FromArgb(255, 222, 173), "NavajoWhite");
        LUT_COLOR.Add(Color.FromArgb(245, 222, 179), "Wheat");
        LUT_COLOR.Add(Color.FromArgb(222, 184, 135), "BurlyWood");
        LUT_COLOR.Add(Color.FromArgb(210, 180, 140), "Tan");
        LUT_COLOR.Add(Color.FromArgb(188, 143, 143), "RosyBrown");
        LUT_COLOR.Add(Color.FromArgb(244, 164, 96), "SandyBrown");
        LUT_COLOR.Add(Color.FromArgb(218, 165, 32), "Goldenrod");
        LUT_COLOR.Add(Color.FromArgb(184, 134, 11), "DarkGoldenrod");
        LUT_COLOR.Add(Color.FromArgb(205, 133, 63), "Peru");
        LUT_COLOR.Add(Color.FromArgb(210, 105, 30), "Chocolate");
        LUT_COLOR.Add(Color.FromArgb(139, 69, 19), "SaddleBrown");
        LUT_COLOR.Add(Color.FromArgb(160, 82, 45), "Sienna");
        LUT_COLOR.Add(Color.FromArgb(165, 42, 42), "Brown");
        LUT_COLOR.Add(Color.FromArgb(128, 0, 0), "Maroon");
        LUT_COLOR.Add(Color.FromArgb(85, 107, 47), "DarkOliveGreen");
        LUT_COLOR.Add(Color.FromArgb(128, 128, 0), "Olive");
        LUT_COLOR.Add(Color.FromArgb(107, 142, 35), "OliveDrab");
        LUT_COLOR.Add(Color.FromArgb(154, 205, 50), "YellowGreen");
        LUT_COLOR.Add(Color.FromArgb(50, 205, 50), "LimeGreen");
        LUT_COLOR.Add(Color.FromArgb(0, 255, 0), "Lime");
        LUT_COLOR.Add(Color.FromArgb(124, 252, 0), "LawnGreen");
        LUT_COLOR.Add(Color.FromArgb(127, 255, 0), "Chartreuse");
        LUT_COLOR.Add(Color.FromArgb(173, 255, 47), "GreenYellow");
        LUT_COLOR.Add(Color.FromArgb(0, 255, 127), "SpringGreen");
        LUT_COLOR.Add(Color.FromArgb(0, 250, 154), "MediumSpringGreen");
        LUT_COLOR.Add(Color.FromArgb(144, 238, 144), "LightGreen");
        LUT_COLOR.Add(Color.FromArgb(152, 251, 152), "PaleGreen");
        LUT_COLOR.Add(Color.FromArgb(143, 188, 143), "DarkSeaGreen");
        LUT_COLOR.Add(Color.FromArgb(60, 179, 113), "MediumSeaGreen");
        LUT_COLOR.Add(Color.FromArgb(46, 139, 87), "SeaGreen");
        LUT_COLOR.Add(Color.FromArgb(34, 139, 34), "ForestGreen");
        LUT_COLOR.Add(Color.FromArgb(0, 128, 0), "Green");
        LUT_COLOR.Add(Color.FromArgb(0, 100, 0), "DarkGreen");
        LUT_COLOR.Add(Color.FromArgb(102, 205, 170), "MediumAquamarine");
        LUT_COLOR.Add(Color.FromArgb(0, 255, 255), "Aqua/Cyan");
        LUT_COLOR.Add(Color.FromArgb(224, 255, 255), "LightCyan");
        LUT_COLOR.Add(Color.FromArgb(175, 238, 238), "PaleTurquoise");
        LUT_COLOR.Add(Color.FromArgb(127, 255, 212), "Aquamarine");
        LUT_COLOR.Add(Color.FromArgb(64, 224, 208), "Turquoise");
        LUT_COLOR.Add(Color.FromArgb(72, 209, 204), "MediumTurquoise");
        LUT_COLOR.Add(Color.FromArgb(0, 206, 209), "DarkTurquoise");
        LUT_COLOR.Add(Color.FromArgb(32, 178, 170), "LightSeaGreen");
        LUT_COLOR.Add(Color.FromArgb(95, 158, 160), "CadetBlue");
        LUT_COLOR.Add(Color.FromArgb(0, 139, 139), "DarkCyan");
        LUT_COLOR.Add(Color.FromArgb(0, 128, 128), "Teal");
        LUT_COLOR.Add(Color.FromArgb(176, 196, 222), "LightSteelBlue");
        LUT_COLOR.Add(Color.FromArgb(176, 224, 230), "PowderBlue");
        LUT_COLOR.Add(Color.FromArgb(173, 216, 230), "LightBlue");
        LUT_COLOR.Add(Color.FromArgb(135, 206, 235), "SkyBlue");
        LUT_COLOR.Add(Color.FromArgb(135, 206, 250), "LightSkyBlue");
        LUT_COLOR.Add(Color.FromArgb(0, 191, 255), "DeepSkyBlue");
        LUT_COLOR.Add(Color.FromArgb(30, 144, 255), "DodgerBlue");
        LUT_COLOR.Add(Color.FromArgb(100, 149, 237), "CornflowerBlue");
        LUT_COLOR.Add(Color.FromArgb(70, 130, 180), "SteelBlue");
        LUT_COLOR.Add(Color.FromArgb(65, 105, 225), "RoyalBlue");
        LUT_COLOR.Add(Color.FromArgb(0, 0, 255), "Blue");
        LUT_COLOR.Add(Color.FromArgb(0, 0, 205), "MediumBlue");
        LUT_COLOR.Add(Color.FromArgb(0, 0, 139), "DarkBlue");
        LUT_COLOR.Add(Color.FromArgb(0, 0, 128), "Navy");
        LUT_COLOR.Add(Color.FromArgb(25, 25, 112), "MidnightBlue");
        LUT_COLOR.Add(Color.FromArgb(230, 230, 250), "Lavender");
        LUT_COLOR.Add(Color.FromArgb(216, 191, 216), "Thistle");
        LUT_COLOR.Add(Color.FromArgb(221, 160, 221), "Plum");
        LUT_COLOR.Add(Color.FromArgb(238, 130, 238), "Violet");
        LUT_COLOR.Add(Color.FromArgb(218, 112, 214), "Orchid");
        LUT_COLOR.Add(Color.FromArgb(255, 0, 255), "Fuchsia/Magenta");
        LUT_COLOR.Add(Color.FromArgb(186, 85, 211), "MediumOrchid");
        LUT_COLOR.Add(Color.FromArgb(147, 112, 219), "MediumPurple");
        LUT_COLOR.Add(Color.FromArgb(138, 43, 226), "BlueViolet");
        LUT_COLOR.Add(Color.FromArgb(148, 0, 211), "DarkViolet");
        LUT_COLOR.Add(Color.FromArgb(153, 50, 204), "DarkOrchid");
        LUT_COLOR.Add(Color.FromArgb(139, 0, 139), "DarkMagenta");
        LUT_COLOR.Add(Color.FromArgb(128, 0, 128), "Purple");
        LUT_COLOR.Add(Color.FromArgb(75, 0, 130), "Indigo");
        LUT_COLOR.Add(Color.FromArgb(72, 61, 139), "DarkSlateBlue");
        LUT_COLOR.Add(Color.FromArgb(102, 51, 153), "RebeccaPurple");
        LUT_COLOR.Add(Color.FromArgb(106, 90, 205), "SlateBlue");
        LUT_COLOR.Add(Color.FromArgb(123, 104, 238), "MediumSlateBlue");
        LUT_COLOR.Add(Color.FromArgb(255, 255, 255), "White");
        LUT_COLOR.Add(Color.FromArgb(255, 250, 250), "Snow");
        LUT_COLOR.Add(Color.FromArgb(240, 255, 240), "Honeydew");
        LUT_COLOR.Add(Color.FromArgb(245, 255, 250), "MintCream");
        LUT_COLOR.Add(Color.FromArgb(240, 255, 255), "Azure");
        LUT_COLOR.Add(Color.FromArgb(240, 248, 255), "AliceBlue");
        LUT_COLOR.Add(Color.FromArgb(248, 248, 255), "GhostWhite");
        LUT_COLOR.Add(Color.FromArgb(245, 245, 245), "WhiteSmoke");
        LUT_COLOR.Add(Color.FromArgb(255, 245, 238), "Seashell");
        LUT_COLOR.Add(Color.FromArgb(245, 245, 220), "Beige");
        LUT_COLOR.Add(Color.FromArgb(253, 245, 230), "OldLace");
        LUT_COLOR.Add(Color.FromArgb(255, 250, 240), "FloralWhite");
        LUT_COLOR.Add(Color.FromArgb(255, 255, 240), "Ivory");
        LUT_COLOR.Add(Color.FromArgb(250, 235, 215), "AntiqueWhite");
        LUT_COLOR.Add(Color.FromArgb(250, 240, 230), "Linen");
        LUT_COLOR.Add(Color.FromArgb(255, 240, 245), "LavenderBlush");
        LUT_COLOR.Add(Color.FromArgb(255, 228, 225), "MistyRose");
        LUT_COLOR.Add(Color.FromArgb(220, 220, 220), "Gainsboro");
        LUT_COLOR.Add(Color.FromArgb(211, 211, 211), "LightGrey");
        LUT_COLOR.Add(Color.FromArgb(192, 192, 192), "Silver");
        LUT_COLOR.Add(Color.FromArgb(169, 169, 169), "DarkGray");
        LUT_COLOR.Add(Color.FromArgb(128, 128, 128), "Gray");
        LUT_COLOR.Add(Color.FromArgb(105, 105, 105), "DimGray");
        LUT_COLOR.Add(Color.FromArgb(119, 136, 153), "LightSlateGray");
        LUT_COLOR.Add(Color.FromArgb(112, 128, 144), "SlateGray");
        LUT_COLOR.Add(Color.FromArgb(47, 79, 79), "DarkSlateGray");
        LUT_COLOR.Add(Color.FromArgb(0, 0, 0), "Black");
    }
}
