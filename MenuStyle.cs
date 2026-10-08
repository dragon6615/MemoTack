using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace MemoTack;

/// <summary>
/// 選單外觀：WinForms 預設的 ToolStrip 是 Office 2003 的漸層風格，跟 Windows 11 不搭。
/// 這裡改成白底、圓角選取框、較寬鬆的項目高度；Windows 11 上再交給 DWM 畫圓角外框與陰影。
/// 右鍵選單、系統匣選單、色盤都用 MenuStyle.Apply 套用。
/// </summary>
internal static class MenuStyle
{
    /// <summary>Windows 11（build 22000）起 DWM 才支援圓角，之前的版本自己畫直角外框</summary>
    private static readonly bool RoundedByDwm = Environment.OSVersion.Version.Build >= 22000;

    private static readonly MenuRenderer Renderer = new();

    /// <summary>套用到選單本身，以及之後加入的所有子選單（子選單常在開啟時才重建）</summary>
    public static void Apply(ToolStripDropDown menu)
    {
        menu.Renderer = Renderer;
        menu.Padding = Scale(menu, new Padding(4, 4, 4, 4));
        if (RoundedByDwm)
        {
            menu.DropShadowEnabled = false; // 系統的 CS_DROPSHADOW 是直角陰影，改用 DWM 的圓角陰影
            menu.HandleCreated += (_, _) => ApplyDwm(menu.Handle);
            if (menu.IsHandleCreated)
                ApplyDwm(menu.Handle);
        }

        foreach (ToolStripItem item in menu.Items)
            StyleItem(menu, item);
        menu.ItemAdded += (_, e) => { if (e.Item != null) StyleItem(menu, e.Item); };
    }

    private static void StyleItem(ToolStripDropDown menu, ToolStripItem item)
    {
        if (item is ToolStripMenuItem menuItem)
        {
            menuItem.Padding = Scale(menu, new Padding(0, 3, 0, 3));
            Apply(menuItem.DropDown);
        }
    }

    private static Padding Scale(Control c, Padding p)
    {
        float k = c.DeviceDpi / 96f;
        return new Padding((int)(p.Left * k), (int)(p.Top * k), (int)(p.Right * k), (int)(p.Bottom * k));
    }

    // ---------- Windows 11 圓角與陰影 ----------

    private const int DWMWA_NCRENDERING_POLICY = 2;
    private const int DWMNCRP_ENABLED = 2;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUNDSMALL = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS { public int Left, Right, Top, Bottom; }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    private static void ApplyDwm(IntPtr hwnd)
    {
        try
        {
            int pref = DWMWCP_ROUNDSMALL;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
            int policy = DWMNCRP_ENABLED;
            DwmSetWindowAttribute(hwnd, DWMWA_NCRENDERING_POLICY, ref policy, sizeof(int));
            var margins = new MARGINS { Left = 1, Right = 1, Top = 1, Bottom = 1 };
            DwmExtendFrameIntoClientArea(hwnd, ref margins);
        }
        catch
        {
            // 失敗就維持系統預設外觀，不影響功能
        }
    }

    // ---------- 繪製 ----------

    private sealed class MenuRenderer : ToolStripProfessionalRenderer
    {
        public MenuRenderer() : base(new Colors()) => RoundedEdges = false;

        /// <summary>先把底色、圖示欄等全部統一成白底，細節再由下面的 override 自己畫</summary>
        private sealed class Colors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => Theme.MenuBack;
            public override Color ImageMarginGradientBegin => Theme.MenuBack;
            public override Color ImageMarginGradientMiddle => Theme.MenuBack;
            public override Color ImageMarginGradientEnd => Theme.MenuBack;
            public override Color MenuBorder => Theme.MenuBorder;
            public override Color MenuItemBorder => Color.Transparent;
            public override Color MenuItemSelected => Theme.MenuHover;
            public override Color SeparatorDark => Theme.MenuSeparator;
            public override Color SeparatorLight => Theme.MenuBack;
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (!RoundedByDwm) // Windows 11 由 DWM 畫圓角外框
                base.OnRenderToolStripBorder(e);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled)
                return;
            int inset = Math.Max(1, e.ToolStrip?.DeviceDpi / 96 ?? 1);
            var rect = new Rectangle(inset, 0, e.Item.Width - inset * 2, e.Item.Height);
            using var path = Theme.RoundedRect(rect, 4 * inset);
            using var brush = new SolidBrush(Theme.MenuHover);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.FillPath(brush, path);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            int margin = e.Item.Height / 2;
            using var pen = new Pen(Theme.MenuSeparator);
            e.Graphics.DrawLine(pen, margin, y, e.Item.Width - margin, y);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            // 右側的快捷鍵或語法提示用淡色，主文字才是重點
            bool isShortcut = e.Item is ToolStripMenuItem m && e.Text == m.ShortcutKeyDisplayString;
            e.TextColor = !e.Item.Enabled ? Theme.MenuTextDisabled
                        : isShortcut ? Theme.MenuTextMuted
                        : Theme.MenuText;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item?.Enabled == false ? Theme.MenuTextDisabled : Theme.MenuTextMuted;
            base.OnRenderArrow(e);
        }

        /// <summary>打勾改用圖示字型的勾號，不畫預設的藍框方塊</summary>
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            using var font = new Font(Theme.IconFontFamily, e.Item.Font.Size);
            TextRenderer.DrawText(e.Graphics, Theme.IconCheck, font, e.ImageRectangle, Theme.MenuText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}
