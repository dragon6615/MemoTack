namespace MemoTack;

/// <summary>
/// 便箋的配色與圖示集中在這裡：調整外觀只改這個檔案，不必在各處找寫死的顏色。
/// 對話框的配色在 StyledDialog。
/// </summary>
internal static class Theme
{
    /// <summary>便箋色盤。ColorIndex 以整數存檔，新顏色只能加在尾端（舊版讀到超出範圍的值會取餘數）</summary>
    public static readonly NoteColor[] Palette =
    {
        new("黃", Color.FromArgb(255, 242, 171), Color.FromArgb(248, 224, 118)),
        new("綠", Color.FromArgb(208, 240, 192), Color.FromArgb(175, 222, 151)),
        new("粉紅", Color.FromArgb(255, 216, 224), Color.FromArgb(245, 183, 196)),
        new("藍", Color.FromArgb(205, 229, 255), Color.FromArgb(166, 205, 243)),
        new("紫", Color.FromArgb(234, 220, 255), Color.FromArgb(213, 191, 245)),
        new("灰", Color.FromArgb(236, 236, 236), Color.FromArgb(215, 215, 215)),
        new("炭黑", Color.FromArgb(58, 58, 60), Color.FromArgb(44, 44, 46), Dark: true),
    };

    // ---------- 自訂顏色：色相自由、亮度有護欄 ----------
    // 不管挑哪個色相，都換算成跟預設色盤一樣柔和的淡色：不會刺眼，深色字也一定讀得清楚。
    // 用 OKLCH（感知均勻色彩空間）固定「看起來的」明度與彩度、只換色相；
    // HSL 或相對亮度都不行：同樣數值的黃綠色會比藍紫色鮮豔刺眼得多。

    private const double BodyLightness = 0.93, BodyChroma = 0.065;   // 內容區
    private const double HeaderLightness = 0.86, HeaderChroma = 0.09; // 標題列

    public static NoteColor FromHue(int hue)
    {
        hue = ((hue % 360) + 360) % 360;
        return new NoteColor("自訂", FromOklch(BodyLightness, BodyChroma, hue), FromOklch(HeaderLightness, HeaderChroma, hue));
    }

    /// <summary>OKLCH → sRGB。超出 sRGB 色域時降低彩度直到能顯示（色相與明度不變）</summary>
    private static Color FromOklch(double lightness, double chroma, double hue)
    {
        double rad = hue * Math.PI / 180;
        for (double c = chroma; c >= 0; c -= 0.005)
        {
            if (OklabToSrgb(lightness, c * Math.Cos(rad), c * Math.Sin(rad)) is { } color)
                return color;
        }
        return OklabToSrgb(lightness, 0, 0) ?? Color.White;
    }

    /// <summary>Björn Ottosson 的 OKLab → sRGB 公式；超出色域回傳 null</summary>
    private static Color? OklabToSrgb(double L, double a, double b)
    {
        double l = Math.Pow(L + 0.3963377774 * a + 0.2158037573 * b, 3);
        double m = Math.Pow(L - 0.1055613458 * a - 0.0638541728 * b, 3);
        double s = Math.Pow(L - 0.0894841775 * a - 1.2914855480 * b, 3);
        double r = 4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s;
        double g = -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s;
        double bl = -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s;
        if (r is < -0.0001 or > 1.0001 || g is < -0.0001 or > 1.0001 || bl is < -0.0001 or > 1.0001)
            return null;

        static int Encode(double v)
        {
            v = Math.Clamp(v, 0, 1);
            double srgb = v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;
            return (int)Math.Round(srgb * 255);
        }
        return Color.FromArgb(Encode(r), Encode(g), Encode(bl));
    }

    public static readonly Color NoteText = Color.FromArgb(50, 50, 50);       // 內文、便箋名稱
    public static readonly Color NoteTextMuted = Color.FromArgb(90, 90, 90);  // 提醒時間
    public static readonly Color NoteGlyph = Color.FromArgb(70, 70, 70);      // 標題列圖示

    /// <summary>響鈴時標題列的閃爍色，也是提醒列的底色</summary>
    public static readonly Color Alert = Color.FromArgb(255, 183, 77);
    public static readonly Color AlertButton = Color.FromArgb(255, 224, 178);
    public static readonly Color AlertButtonHover = Color.FromArgb(255, 236, 204);
    public static readonly Color AlertText = Color.FromArgb(60, 60, 60);

    public static readonly Color MenuBack = Color.White;
    public static readonly Color MenuHover = Color.FromArgb(238, 238, 238);
    public static readonly Color MenuBorder = Color.FromArgb(214, 214, 214);   // Windows 10 才畫，11 由 DWM 畫
    public static readonly Color MenuSeparator = Color.FromArgb(229, 229, 229);
    public static readonly Color MenuText = Color.FromArgb(32, 32, 32);
    public static readonly Color MenuTextMuted = Color.FromArgb(120, 120, 120);
    public static readonly Color MenuTextDisabled = Color.FromArgb(170, 170, 170);
    public static readonly Color SwatchRing = Color.FromArgb(70, 70, 70);

    /// <summary>按比例變暗（hover、按下、描邊用）</summary>
    public static Color Darken(Color c, float amount = 0.15f) => Blend(c, Color.Black, amount);

    /// <summary>a 往 b 混合 t（0 = a、1 = b）</summary>
    public static Color Blend(Color a, Color b, float t) => Color.FromArgb(
        (int)Math.Round(a.R + (b.R - a.R) * t),
        (int)Math.Round(a.G + (b.G - a.G) * t),
        (int)Math.Round(a.B + (b.B - a.B) * t));

    /// <summary>圓角矩形路徑（hover 底色、選單選取框用）</summary>
    public static System.Drawing.Drawing2D.GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        if (d <= 0)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    // ---------- 圖示字型 ----------

    /// <summary>
    /// 系統內建的圖示字型：Windows 11 的 Segoe Fluent Icons，Windows 10 備援 Segoe MDL2 Assets（兩者字碼相同）。
    /// 單色、粗細一致、隨 DPI 縮放，取代顏色與筆畫不一的 emoji。.NET 8 只支援 Windows 10 以上，必定有其中一個。
    /// </summary>
    public static readonly string IconFontFamily = ResolveIconFont();

    private static string ResolveIconFont()
    {
        using var installed = new System.Drawing.Text.InstalledFontCollection();
        return installed.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
    }

    public const string IconColor = "";    // 調色盤
    public const string IconDelete = "";   // 垃圾桶
    public const string IconAdd = "";      // ＋
    public const string IconClose = "";    // ✕
    public const string IconReminder = ""; // 鈴鐺
    public const string IconCheck = "";    // 勾號（選單打勾）
    public const string IconInfo = "";     // 關於
    public const string IconSettings = ""; // 設定（齒輪）
    public const string IconUpdate = "";   // 更新（下載）
}

/// <summary>
/// 一種便箋顏色。深色（Dark）便箋的文字改用淺色，hover 改成變亮；
/// 對話框的頂端色帶用 Accent（深色便箋給淺灰，深底配深字會看不清楚）。
/// </summary>
internal sealed record NoteColor(string Name, Color Body, Color Header, bool Dark = false)
{
    public Color Text => Dark ? Color.FromArgb(232, 232, 232) : Theme.NoteText;
    public Color TextMuted => Dark ? Color.FromArgb(175, 175, 175) : Theme.NoteTextMuted;
    public Color Glyph => Dark ? Color.FromArgb(215, 215, 215) : Theme.NoteGlyph;
    public Color Accent => Dark ? Color.FromArgb(222, 222, 222) : Header;

    /// <summary>標題列按鈕 hover／按下的底色：淺色便箋變暗、深色便箋變亮</summary>
    public Color Hover(Color header, float amount) =>
        Dark ? Theme.Blend(header, Color.White, amount) : Theme.Darken(header, amount);

    /// <summary>視窗外框、縮放把手點點：比標題列再深（深色便箋則再亮）一點</summary>
    public Color Outline => Dark ? Theme.Blend(Header, Color.White, 0.18f) : Theme.Darken(Header, 0.15f);
}
