using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace MemoTack;

/// <summary>
/// 單張便箋視窗：無邊框、置頂、可拖曳標題列移動、邊緣縮放、切換顏色。
/// </summary>
public class NoteForm : Form
{
    // ---- 預設色盤：(內容背景, 標題列) ----
    private static readonly (Color Body, Color Header)[] Palette =
    {
        (Color.FromArgb(255, 242, 171), Color.FromArgb(248, 224, 118)), // 黃
        (Color.FromArgb(208, 240, 192), Color.FromArgb(175, 222, 151)), // 綠
        (Color.FromArgb(255, 216, 224), Color.FromArgb(245, 183, 196)), // 粉紅
        (Color.FromArgb(205, 229, 255), Color.FromArgb(166, 205, 243)), // 藍
    };

    private const int GripSize = 6;      // 邊緣縮放感應區（同時是視覺留白）
    private const int TitleHeight = 32;  // 標題列高度

    private readonly NoteData _data;
    private readonly AppSettings _settings;
    private readonly Panel _titleBar;
    private readonly Panel _contentPanel;
    private readonly TextBox _textBox;
    private readonly Button _btnColor;
    private readonly Button _btnDelete;
    private readonly Button _btnNew;
    private readonly Button _btnClose;

    /// <summary>使用者按「＋」要求新增一張便箋</summary>
    public event Action<NoteForm>? NewNoteRequested;

    /// <summary>使用者按「✕」（或 Alt+F4）要求關閉此便箋（保留資料，可從系統匣再開啟）</summary>
    public event Action<NoteForm>? CloseRequested;

    /// <summary>使用者按「🗑」要求永久刪除此便箋（已經過確認）</summary>
    public event Action<NoteForm>? DeleteRequested;

    /// <summary>使用者按 Ctrl+S 要求立即存檔</summary>
    public event Action<NoteForm>? SaveRequested;

    /// <summary>任何需要保存的狀態變更（移動/縮放/內容/顏色/字級）</summary>
    public event Action? Changed;

    public NoteForm(NoteData data, AppSettings settings)
    {
        _data = data;
        _settings = settings;

        // ---- 視窗基本設定 ----
        FormBorderStyle = FormBorderStyle.None; // 無邊框
        ShowInTaskbar = false;                   // 不出現在工具列（置頂與否由 ApplySettings 依設定套用）
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(160, 120);
        Padding = new Padding(GripSize);         // 留出邊緣，讓 WM_NCHITTEST 能收到縮放區的滑鼠事件
        Bounds = ClampToScreen(new Rectangle(data.X, data.Y, data.Width, data.Height));

        // ---- 標題列 ----
        _titleBar = new Panel { Dock = DockStyle.Top, Height = TitleHeight };
        _titleBar.MouseDown += TitleBar_MouseDown;

        // 顏色按鈕：不用文字，改為自繪「下一個顏色」的圓形色票（見 BtnColor_Paint）
        _btnColor = MakeTitleButton("", "切換顏色");
        _btnColor.Dock = DockStyle.Left;
        _btnColor.Click += (_, _) => CycleColor();
        _btnColor.Paint += BtnColor_Paint;

        // 刪除按鈕：放在左側、遠離 ✕，避免誤按
        _btnDelete = MakeTitleButton("🗑", "永久刪除此便箋");
        _btnDelete.Dock = DockStyle.Left;
        _btnDelete.Click += BtnDelete_Click;

        _btnClose = MakeTitleButton("✕", "關閉此便箋（保留內容，可從系統匣再開啟）");
        _btnClose.Dock = DockStyle.Right;
        _btnClose.Click += (_, _) => CloseRequested?.Invoke(this);

        _btnNew = MakeTitleButton("＋", "新增便箋");
        _btnNew.Dock = DockStyle.Right;
        _btnNew.Click += (_, _) => NewNoteRequested?.Invoke(this);

        // Dock 佈局依 Controls 反序處理：
        // 加入順序 ＋、✕、🗑、● → 佈局順序 ●(最左)、🗑(其右)、✕(最右)、＋(其左)
        _titleBar.Controls.Add(_btnNew);
        _titleBar.Controls.Add(_btnClose);
        _titleBar.Controls.Add(_btnDelete);
        _titleBar.Controls.Add(_btnColor);

        // ---- 內容文字框 ----
        _textBox = new TextBox
        {
            Multiline = true,
            WordWrap = true,                    // 自動換行
            AcceptsReturn = true,
            AcceptsTab = true,
            ScrollBars = ScrollBars.None,       // 隱藏捲軸較美觀（滾輪、方向鍵仍可捲動）
            BorderStyle = BorderStyle.None,
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(50, 50, 50),
            Text = data.Content,
        };
        _textBox.MouseWheel += TextBox_MouseWheel;              // Ctrl+滾輪 調整字型大小
        _textBox.TextChanged += (_, _) => Changed?.Invoke();    // 內容變更 → 通知存檔

        // 外包一層 Panel 給文字內距，看起來不那麼擠
        _contentPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6, 5, 6, 5) };
        _contentPanel.Controls.Add(_textBox);

        Controls.Add(_titleBar);
        Controls.Add(_contentPanel);
        _contentPanel.BringToFront(); // 讓 Fill 在 Top 之後佈局，避免被標題列蓋住

        ApplyColor();
        ApplySettings();
    }

    /// <summary>
    /// 套用全域外觀設定（標題列/內容字型）。設定視窗按「確定」後也會被呼叫。
    /// resetContentSize=true 時把內容字級重設為設定值（覆蓋 Ctrl+滾輪 的個別調整）。
    /// </summary>
    public void ApplySettings(bool resetContentSize = false)
    {
        if (resetContentSize)
            _data.FontSize = _settings.ContentFontSize;

        // 置頂與否依設定
        TopMost = _settings.AlwaysOnTop;

        // 標題列：字型設定
        _btnColor.Font = new Font(_settings.TitleFontFamily, _settings.TitleFontSize);
        _btnDelete.Font = new Font(_settings.TitleFontFamily, _settings.TitleFontSize + 1f);
        _btnNew.Font = new Font(_settings.TitleFontFamily, _settings.TitleFontSize + 3f);
        _btnClose.Font = new Font(_settings.TitleFontFamily, _settings.TitleFontSize + 2f);

        // 標題列高度：用最大的按鈕字型「實際高度」計算，字型改多大就跟著多高
        using (var probe = new Font(_settings.TitleFontFamily, _settings.TitleFontSize + 3f))
            _titleBar.Height = Math.Max(26, (int)Math.Ceiling(probe.GetHeight()) + 10);

        int btnWidth = Math.Max(34, _titleBar.Height + 6);
        _btnColor.Width = _btnDelete.Width = _btnNew.Width = _btnClose.Width = btnWidth;

        // 內容：字型用全域設定，字級用便箋自己的值
        _textBox.Font = new Font(_settings.ContentFontFamily, _data.FontSize);
    }

    /// <summary>刪除前先確認（有內容時），確認後才發出 DeleteRequested</summary>
    private void BtnDelete_Click(object? sender, EventArgs e)
    {
        if (_textBox.TextLength > 0)
        {
            var result = MessageBox.Show(this,
                "確定要永久刪除這張便箋？刪除後無法復原。",
                "刪除便箋", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (result != DialogResult.Yes)
                return;
        }
        DeleteRequested?.Invoke(this);
    }

    /// <summary>把目前 UI 狀態寫回資料物件並回傳（存檔用）</summary>
    public NoteData ToData()
    {
        _data.Content = _textBox.Text;
        _data.X = Left;
        _data.Y = Top;
        _data.Width = Width;
        _data.Height = Height;
        return _data;
    }

    // ---------- 顏色 ----------

    private void CycleColor()
    {
        _data.ColorIndex = (_data.ColorIndex + 1) % Palette.Length;
        ApplyColor();
        _btnColor.Invalidate(); // 重畫色票，顯示新的「下一個顏色」
        Changed?.Invoke();
    }

    /// <summary>
    /// 顏色按鈕自繪：畫一個「下一個顏色」的圓形色票，
    /// 讓使用者預覽點下去會換成什麼顏色。
    /// </summary>
    private void BtnColor_Paint(object? sender, PaintEventArgs e)
    {
        var next = Palette[(_data.ColorIndex + 1) % Palette.Length].Body;
        const int d = 16; // 色票直徑
        var rect = new Rectangle((_btnColor.Width - d) / 2, (_btnColor.Height - d) / 2, d, d);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(next);
        e.Graphics.FillEllipse(brush, rect);
        using var pen = new Pen(Darken(Darken(next)));
        e.Graphics.DrawEllipse(pen, rect);
    }

    private void ApplyColor()
    {
        var (body, header) = Palette[_data.ColorIndex % Palette.Length];
        BackColor = body;               // Padding 邊緣也會呈現內容色
        _contentPanel.BackColor = body;
        _textBox.BackColor = body;
        _titleBar.BackColor = header;
        foreach (Button b in new[] { _btnColor, _btnDelete, _btnNew, _btnClose })
        {
            b.BackColor = header;
            b.FlatAppearance.MouseOverBackColor = Darken(header);
            b.FlatAppearance.MouseDownBackColor = Darken(Darken(header));
        }
    }

    private static Color Darken(Color c) =>
        Color.FromArgb(c.R * 85 / 100, c.G * 85 / 100, c.B * 85 / 100);

    private static Button MakeTitleButton(string text, string tooltip)
    {
        var btn = new Button
        {
            Text = text,
            Width = 38,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10f),
            ForeColor = Color.FromArgb(80, 80, 80),
            TabStop = false,
            Cursor = Cursors.Hand,
        };
        btn.FlatAppearance.BorderSize = 0;
        new ToolTip().SetToolTip(btn, tooltip);
        return btn;
    }

    // ---------- 字型大小（Ctrl+滾輪） ----------

    private void TextBox_MouseWheel(object? sender, MouseEventArgs e)
    {
        if (ModifierKeys != Keys.Control) return;
        if (e is HandledMouseEventArgs h) h.Handled = true; // 不要同時捲動

        float size = Math.Clamp(_data.FontSize + (e.Delta > 0 ? 1f : -1f), 8f, 32f);
        if (Math.Abs(size - _data.FontSize) < 0.1f) return;

        _data.FontSize = size;
        var old = _textBox.Font;
        _textBox.Font = new Font(old.FontFamily, size);
        old.Dispose();
        Changed?.Invoke();
    }

    // 移動 / 縮放 → 通知存檔（拖曳過程會連續觸發，由管理端防抖）
    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        Changed?.Invoke();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        Changed?.Invoke();
    }

    // ---------- 便箋內快捷鍵 ----------

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.N: // 新增便箋
                NewNoteRequested?.Invoke(this);
                return true;

            case Keys.Control | Keys.W: // 關閉便箋（保留內容）
                CloseRequested?.Invoke(this);
                return true;

            case Keys.Control | Keys.S: // 立即存檔
                SaveRequested?.Invoke(this);
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ---------- 視窗樣式：不出現在 Alt+Tab ----------

    private const int WS_EX_TOOLWINDOW = 0x80;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW; // 工具視窗：Alt+Tab 清單不會出現便箋
            return cp;
        }
    }

    // ---------- 圓角（Windows 11 原生 DWM 圓角） ----------

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            int pref = DWMWCP_ROUND;
            DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
        }
        catch
        {
            // Windows 10 以下沒有此 API：維持直角，不影響功能
        }
    }

    // ---------- 拖曳移動（標題列） ----------

    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HTCAPTION = 2;

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    private void TitleBar_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            // 交給系統做原生視窗拖曳：流暢且支援貼齊
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
        }
    }

    // ---------- 邊緣縮放（WM_NCHITTEST） ----------

    private const int WM_NCHITTEST = 0x84;
    private const int HTCLIENT = 1;
    private const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13,
                      HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_NCHITTEST)
        {
            base.WndProc(ref m);
            if ((int)m.Result == HTCLIENT)
            {
                // 螢幕座標 → 視窗座標（處理多螢幕負座標）
                int lp = unchecked((int)(long)m.LParam);
                var pos = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));

                bool left = pos.X < GripSize;
                bool right = pos.X >= Width - GripSize;
                bool top = pos.Y < GripSize;
                bool bottom = pos.Y >= Height - GripSize;

                if (top && left) m.Result = HTTOPLEFT;
                else if (top && right) m.Result = HTTOPRIGHT;
                else if (bottom && left) m.Result = HTBOTTOMLEFT;
                else if (bottom && right) m.Result = HTBOTTOMRIGHT;
                else if (left) m.Result = HTLEFT;
                else if (right) m.Result = HTRIGHT;
                else if (top) m.Result = HTTOP;
                else if (bottom) m.Result = HTBOTTOM;
            }
            return;
        }
        base.WndProc(ref m);
    }

    // ---------- 關閉行為 ----------

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // 使用者主動關閉（如 Alt+F4）視同按「×」＝刪除，統一交由管理端處理；
        // 程式結束（ApplicationExitCall）則直接放行
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            CloseRequested?.Invoke(this);
            return;
        }
        base.OnFormClosing(e);
    }

    // ---------- 工具 ----------

    /// <summary>確保還原的視窗落在可見螢幕範圍內（例如拔掉外接螢幕後）</summary>
    private static Rectangle ClampToScreen(Rectangle r)
    {
        if (r.Width < 160) r.Width = 640;
        if (r.Height < 120) r.Height = 480;

        var screen = SystemInformation.VirtualScreen;
        r.X = Math.Max(screen.Left, Math.Min(r.X, screen.Right - r.Width));
        r.Y = Math.Max(screen.Top, Math.Min(r.Y, screen.Bottom - r.Height));
        return r;
    }
}
