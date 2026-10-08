namespace MemoTack;

/// <summary>
/// 對話框共用外觀（設定、設定提醒）：微軟正黑體 UI、頂端色帶、白色內容區、灰底按鈕列。
/// 版面全部依內容自動決定大小（不寫死寬高），任何 DPI 與字型下都不會切字；
/// 間距以 96 DPI 為基準由 Dpi() 換算。
/// </summary>
public abstract class StyledDialog : Form
{
    protected static readonly Color Surface = Color.White;
    protected static readonly Color FooterBack = Color.FromArgb(247, 247, 247);
    protected static readonly Color ChipBack = Color.FromArgb(242, 242, 242);
    protected static readonly Color ChipHover = Color.FromArgb(228, 228, 228);
    protected static readonly Color ChipBorder = Color.FromArgb(214, 214, 214);
    protected static readonly Color TextStrong = Color.FromArgb(40, 40, 40);
    protected static readonly Color TextMuted = Color.FromArgb(110, 110, 110);
    protected static readonly Color Primary = Color.FromArgb(0, 103, 192); // Windows 11 主要按鈕藍
    protected static readonly Color Danger = Color.FromArgb(196, 43, 28);

    private readonly List<Font> _fonts = new();

    protected StyledDialog(string title)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true; // 便箋可能設為置頂，對話框也要置頂才不會被蓋住
        BackColor = Surface;
        Font = OwnFont(10f);
        AutoScaleMode = AutoScaleMode.None; // 尺寸都由字型量測與 Dpi() 算出，避免被二次縮放
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
    }

    private TableLayoutPanel? _root;

    /// <summary>由上往下放入色帶、內容、按鈕列</summary>
    protected void SetLayout(Control header, Control body, Control footer)
    {
        _root = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        // 比例欄：版面被撐寬（LockCurrentWidth）時，色帶與按鈕列會跟著延伸到視窗邊緣
        _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _root.Controls.Add(header, 0, 0);
        _root.Controls.Add(body, 0, 1);
        _root.Controls.Add(footer, 0, 2);
        Controls.Add(_root);
    }

    /// <summary>
    /// 以目前的寬度當最小寬度：內容會切換顯示的對話框（例如設定提醒依重複方式顯示不同欄位），
    /// 先切到最寬的版面再呼叫，之後切換時視窗才不會忽寬忽窄。
    /// 要設在內層版面而不是 Form：只撐寬 Form 的話，色帶與按鈕列不會跟著變寬，右邊會露出空白。
    /// </summary>
    protected void LockCurrentWidth()
    {
        if (_root == null)
            return;
        PerformLayout();
        _root.MinimumSize = new Size(_root.Width, 0);
    }

    /// <summary>頂端色帶：圖示 + 大標題 + 副標題。圖示用圖示字型（Theme.Icon*），跟便箋標題列一致</summary>
    protected Control MakeHeader(string icon, string title, string subtitle, Color accent)
    {
        var band = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            BackColor = accent,
            Padding = new Padding(Dpi(20), Dpi(14), Dpi(20), Dpi(14)),
            Margin = Padding.Empty,
            Dock = DockStyle.Fill,
        };
        var titleRow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = Padding.Empty,
        };
        var titleFont = OwnFont(13f, FontStyle.Bold);
        var iconFont = OwnIconFont(14f);
        titleRow.Controls.Add(new Label
        {
            Text = icon,
            AutoSize = true,
            Font = iconFont,
            ForeColor = TextStrong,
            // 圖示字型的行高比文字矮：上方補差距讓兩者垂直置中
            Margin = new Padding(0, Math.Max(0, (titleFont.Height - iconFont.Height) / 2), Dpi(8), 0),
        });
        titleRow.Controls.Add(new Label
        {
            Text = title,
            AutoSize = true,
            Font = titleFont,
            ForeColor = TextStrong,
            Margin = Padding.Empty,
        });
        band.Controls.Add(titleRow);
        band.Controls.Add(new Label
        {
            Text = subtitle,
            AutoSize = true,
            ForeColor = Color.FromArgb(80, 80, 80),
            Margin = new Padding(0, Dpi(4), 0, 0),
        });
        return band;
    }

    /// <summary>內容區：左欄標籤、右欄控制項的兩欄表格</summary>
    protected TableLayoutPanel MakeBody()
    {
        var body = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Padding = new Padding(Dpi(20), Dpi(16), Dpi(20), Dpi(12)),
            Dock = DockStyle.Fill,
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        return body;
    }

    /// <summary>底部按鈕列：left 靠左（可為 null），buttons 靠右依序排列</summary>
    protected Control MakeFooter(Control? left, params Button[] buttons)
    {
        var footer = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2 + buttons.Length,
            RowCount = 1,
            BackColor = FooterBack,
            Padding = new Padding(Dpi(16), Dpi(12), Dpi(16), Dpi(12)),
            Margin = Padding.Empty,
            Dock = DockStyle.Fill,
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // 撐開中間，讓按鈕靠右
        foreach (var _ in buttons)
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        if (left != null)
        {
            left.Anchor = AnchorStyles.Left;
            footer.Controls.Add(left, 0, 0);
        }
        for (int i = 0; i < buttons.Length; i++)
            footer.Controls.Add(buttons[i], 2 + i, 0);
        return footer;
    }

    /// <summary>主要按鈕（藍底白字）或次要按鈕（白底）</summary>
    protected Button MakeButton(string text, bool primary)
    {
        var btn = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(Dpi(84), 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Primary : Color.White,
            ForeColor = primary ? Color.White : TextStrong,
            Padding = new Padding(Dpi(8), Dpi(3), Dpi(8), Dpi(3)),
            Margin = new Padding(Dpi(8), 0, 0, 0),
            Cursor = Cursors.Hand,
        };
        btn.FlatAppearance.BorderColor = primary ? Primary : ChipBorder;
        btn.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(0, 90, 170) : ChipBack;
        return btn;
    }

    /// <summary>橫向排列、不換行、依內容決定大小的容器</summary>
    protected FlowLayoutPanel MakeFlow() => new()
    {
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        WrapContents = false,
        Margin = new Padding(0, Dpi(4), 0, Dpi(4)),
        Padding = Padding.Empty,
    };

    /// <summary>在兩欄表格加一列：左欄灰色標籤、右欄控制項。回傳標籤，方便整列隱藏（AutoSize 列會跟著收起）</summary>
    protected Label AddRow(TableLayoutPanel table, string label, Control control)
    {
        int row = table.RowCount++;
        var caption = new Label
        {
            Text = label,
            AutoSize = true,
            ForeColor = TextMuted,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, Dpi(14), 0),
        };
        table.Controls.Add(caption, 0, row);
        table.Controls.Add(control, 1, row);
        return caption;
    }

    /// <summary>以 96 DPI 為基準的像素值換算成目前 DPI</summary>
    protected int Dpi(int px) => (int)Math.Round(px * DeviceDpi / 96f);

    /// <summary>微軟正黑體 UI：中文比 Segoe UI 的備援字型清楚；系統沒有時會自動退回預設字型</summary>
    protected Font OwnFont(float size, FontStyle style = FontStyle.Regular)
    {
        var f = new Font("Microsoft JhengHei UI", size, style);
        _fonts.Add(f);
        return f;
    }

    protected Font OwnIconFont(float size)
    {
        var f = new Font(Theme.IconFontFamily, size);
        _fonts.Add(f);
        return f;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            foreach (var f in _fonts)
                f.Dispose();
            _fonts.Clear();
        }
    }
}
