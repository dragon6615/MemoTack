using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace MemoTack;

/// <summary>
/// 單張便箋視窗：無邊框、置頂、可拖曳標題列移動、邊緣縮放、切換顏色、提醒響鈴。
/// </summary>
public class NoteForm : Form
{
    private static NoteColor[] Palette => Theme.Palette;
    private NoteColor CurrentColor => _data.CustomHue is int hue ? CustomColor(hue, _data.CustomDark) : Palette[_data.ColorIndex];

    private NoteColor? _customColor; // 自訂色要做色域換算，色相與深淺沒變就沿用
    private (int Hue, bool Dark) _customColorKey = (-1, false);

    private NoteColor CustomColor(int hue, bool dark)
    {
        if (_customColor == null || _customColorKey != (hue, dark))
        {
            _customColor = Theme.FromHue(hue, dark);
            _customColorKey = (hue, dark);
        }
        return _customColor;
    }

    private const int GripSize = 6;      // 邊緣縮放感應區（同時是視覺留白）
    private const int TitleHeight = 32;  // 標題列高度

    private readonly NoteData _data;
    private readonly AppSettings _settings;
    private readonly Panel _titleBar;
    private readonly Panel _contentPanel;
    private readonly MarkdownTextBox _textBox;
    private readonly TitleButton _btnColor;
    private readonly TitleButton _btnDelete;
    private readonly TitleButton _btnNew;
    private readonly TitleButton _btnClose;
    private readonly TitleButton _btnReminder;
    private readonly TitleButton[] _titleButtons;
    private readonly Label _titleLabel;                  // 標題列的便箋名稱
    private readonly SingleLineLabel _reminderLabel;     // 標題列的提醒時間
    private TextBox? _renameBox;               // 改名時疊在名稱上的輸入框
    private readonly ResizeGrip _grip;         // 右下角縮放把手
    private readonly Panel _reminderBar;       // 響鈴時顯示在標題列下方的提醒列
    private readonly SingleLineLabel _reminderBarLabel;
    private readonly Button _btnSnooze;
    private readonly Button _btnDone;
    private readonly System.Windows.Forms.Timer _flashTimer = new() { Interval = 500 };
    private int _flashCount;
    private bool _showQuietly;                 // ShowWithoutFocus 期間為 true
    private readonly ToolTip _toolTip = new();       // 所有按鈕共用，隨便箋一起釋放
    private readonly List<Font> _ownedFonts = new(); // 本便箋建立的字型，替換或關閉時釋放

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
        _data.ColorIndex = ((data.ColorIndex % Palette.Length) + Palette.Length) % Palette.Length; // 防 JSON 裡的負數或超出範圍

        // ---- 視窗基本設定 ----
        FormBorderStyle = FormBorderStyle.None; // 無邊框
        ShowInTaskbar = false;                   // 不出現在工具列（置頂與否由 ApplySettings 依設定套用）
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(160, 120);        // 實際最小寬度由 ApplySettings 依按鈕寬度計算
        Padding = new Padding(GripSize);         // 留出邊緣，讓 WM_NCHITTEST 能收到縮放區的滑鼠事件
        Bounds = ClampToScreen(new Rectangle(data.X, data.Y, data.Width, data.Height));

        // ---- 標題列 ----
        _titleBar = new Panel { Dock = DockStyle.Top, Height = TitleHeight };
        _titleBar.MouseDown += TitleBar_MouseDown;

        // 顏色按鈕：點了跳出四色色盤直接選
        _btnColor = MakeTitleButton(Theme.IconColor, "變更顏色");
        _btnColor.Dock = DockStyle.Left;
        _btnColor.Click += (_, _) => ShowColorMenu();

        // 刪除按鈕：與 ✕ 之間隔著 ⏰，且有內容時會先確認，避免誤按
        _btnDelete = MakeTitleButton(Theme.IconDelete, "永久刪除此便箋");
        _btnDelete.Dock = DockStyle.Right;
        _btnDelete.Click += BtnDelete_Click;

        _btnClose = MakeTitleButton(Theme.IconClose, "關閉此便箋（保留內容，可從系統匣再開啟）");
        _btnClose.Dock = DockStyle.Right;
        _btnClose.Click += (_, _) => CloseRequested?.Invoke(this);

        _btnNew = MakeTitleButton(Theme.IconAdd, "新增便箋");
        _btnNew.Dock = DockStyle.Right;
        _btnNew.Click += (_, _) => NewNoteRequested?.Invoke(this);

        _btnReminder = MakeTitleButton(Theme.IconReminder, "設定提醒");
        _btnReminder.Dock = DockStyle.Right;
        _btnReminder.Click += BtnReminder_Click;

        _titleButtons = new[] { _btnColor, _btnDelete, _btnNew, _btnClose, _btnReminder };

        // 中間空白處：左邊是便箋名稱（填滿剩餘空間），右邊緊鄰 ＋ 的是提醒時間（寬度由 FitReminderLabel 控制）。
        // 兩者都是拖曳區；名稱雙擊可改名，所以拖曳要等滑鼠移動超過門檻才開始，否則收不到雙擊
        _titleLabel = new SingleLineLabel
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Theme.NoteText,
            Padding = new Padding(2, 0, 0, 0),
        };
        _titleLabel.MouseDown += TitleButton_MouseDown;
        _titleLabel.MouseMove += TitleButton_MouseMove;
        _titleLabel.DoubleClick += (_, _) => BeginRename();

        _reminderLabel = new SingleLineLabel
        {
            Dock = DockStyle.Right,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Theme.NoteTextMuted,
            Icon = Theme.IconReminder,
        };
        _reminderLabel.MouseDown += TitleButton_MouseDown;
        _reminderLabel.MouseMove += TitleButton_MouseMove;
        _titleBar.Resize += (_, _) => FitReminderLabel();

        // Dock 佈局依 Controls 反序處理：
        // 加入順序 名稱、時間、＋、🗑、⏰、✕、🎨 → 佈局 🎨(最左)，右側由右往左 ✕、⏰、🗑、＋、時間，名稱填滿中間
        _titleBar.Controls.Add(_titleLabel);
        _titleBar.Controls.Add(_reminderLabel);
        _titleBar.Controls.Add(_btnNew);
        _titleBar.Controls.Add(_btnDelete);
        _titleBar.Controls.Add(_btnReminder);
        _titleBar.Controls.Add(_btnClose);
        _titleBar.Controls.Add(_btnColor);

        // ---- 提醒列（響鈴時才顯示） ----
        _reminderBarLabel = new SingleLineLabel { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Icon = Theme.IconReminder };
        _btnSnooze = MakeBarButton("延後 10 分", "10 分鐘後再提醒一次");
        _btnSnooze.Click += (_, _) => SnoozeReminder();
        _btnDone = MakeBarButton("完成", "不重複的提醒會清除；重複的提醒排到下一次");
        _btnDone.Click += (_, _) => CompleteReminder();
        _reminderBar = new Panel
        {
            Dock = DockStyle.Top,
            BackColor = Theme.Alert,
            Padding = new Padding(6, 3, 4, 3),
            Visible = false,
        };
        // 加入順序 說明、延後、完成 → 佈局順序 完成(最右)、延後(其左)，說明填滿剩餘
        _reminderBar.Controls.Add(_reminderBarLabel);
        _reminderBar.Controls.Add(_btnSnooze);
        _reminderBar.Controls.Add(_btnDone);
        _flashTimer.Tick += FlashTimer_Tick;

        // ---- 內容文字框 ----
        // 內容編輯器：即時套用 markdown 樣式（多行、自動換行、無捲軸、無邊框由 MarkdownTextBox 設定）
        _textBox = new MarkdownTextBox
        {
            Dock = DockStyle.Fill,
            ForeColor = Theme.NoteText,
            Markdown = data.Content, // 待辦在畫面上顯示為 ☐／☑，存檔仍是標準 - [ ]
        };
        _textBox.MouseWheel += TextBox_MouseWheel;              // Ctrl+滾輪 調整字型大小
        _textBox.TextChanged += (_, _) => Changed?.Invoke();    // 內容變更 → 通知存檔

        // 外包一層 Panel 給文字內距，看起來不那麼擠
        // 內距依 DPI 換算（寫死像素在 150% 縮放下會顯得擠）
        _contentPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Dpi(8), Dpi(6), Dpi(8), Dpi(6)) };
        _contentPanel.Controls.Add(_textBox);

        // 加入順序 內容、提醒列、標題列 → 佈局順序 標題列(最上)、提醒列(其下)，內容填滿剩餘
        Controls.Add(_contentPanel);
        Controls.Add(_reminderBar);
        Controls.Add(_titleBar);

        // 右下角縮放把手：疊在內容上方（不參與 Dock 佈局），隨視窗大小固定在右下角
        _grip = new ResizeGrip { Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        int gripSize = (int)Math.Round(16 * DeviceDpi / 96f);
        _grip.SetBounds(ClientSize.Width - GripSize - gripSize, ClientSize.Height - GripSize - gripSize, gripSize, gripSize);
        _grip.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, HTBOTTOMRIGHT, 0); // 交給系統做原生縮放
        };
        Controls.Add(_grip);
        _grip.BringToFront();

        _revealTimer.Tick += RevealTimer_Tick;
        _hoverPoll.Tick += HoverPoll_Tick;
        HookHover(this);

        ApplyColor();
        ApplySettings();
        RefreshTitle();
        RefreshReminderLabel();
        UpdateReveal(animate: false);
    }

    /// <summary>
    /// 套用全域外觀設定（標題列/內容字型）。設定視窗按「確定」後也會被呼叫。
    /// resetContentSize=true 時把內容字級重設為設定值（覆蓋 Ctrl+滾輪 的個別調整）。
    /// </summary>
    public void ApplySettings(bool resetContentSize = false)
    {
        if (resetContentSize)
            _data.FontSize = _settings.ContentFontSize;

        // 先記下舊字型，全部換上新字型後再釋放
        var oldFonts = _ownedFonts.ToList();
        _ownedFonts.Clear();

        // 置頂與否依設定
        TopMost = _settings.AlwaysOnTop;

        // 標題列：按鈕用圖示字型（同一個大小，粗細才一致），文字用設定的字型
        var iconFont = OwnFont(Theme.IconFontFamily, _settings.TitleFontSize);
        foreach (var b in _titleButtons)
            b.Font = iconFont;
        float reminderSize = Math.Max(7f, _settings.TitleFontSize - 1f);
        _reminderLabel.Font = OwnFont(_settings.TitleFontFamily, reminderSize);
        _reminderLabel.IconFont = OwnFont(Theme.IconFontFamily, reminderSize - 1f);
        _titleLabel.Font = OwnFont(_settings.TitleFontFamily, _settings.TitleFontSize, FontStyle.Bold);

        // 標題列高度：用最大的按鈕字型「實際高度」計算，字型改多大就跟著多高
        using (var probe = new Font(_settings.TitleFontFamily, _settings.TitleFontSize + 3f))
            _titleBar.Height = Math.Max(26, (int)Math.Ceiling(probe.GetHeight()) + 10);

        int btnWidth = Math.Max(28, _titleBar.Height); // 正方形按鈕
        foreach (var b in _titleButtons)
            b.Width = btnWidth;

        // 提醒列：說明與按鈕共用一個字型，高度隨字級調整
        var barFont = OwnFont(_settings.TitleFontFamily, _settings.TitleFontSize);
        _reminderBarLabel.Font = _btnSnooze.Font = _btnDone.Font = barFont;
        _reminderBarLabel.IconFont = iconFont;
        _reminderBar.Height = Math.Max(28, (int)Math.Ceiling(barFont.GetHeight()) + 14);

        // 內容：字型用全域設定，字級用便箋自己的值
        _textBox.Font = OwnFont(_settings.ContentFontFamily, _data.FontSize);

        foreach (var f in oldFonts)
            f.Dispose();

        FitReminderLabel(); // 字型變了，提醒時間需要的寬度也變了
    }

    private Font OwnFont(string family, float size, FontStyle style = FontStyle.Regular)
    {
        var font = new Font(family, size, style);
        _ownedFonts.Add(font);
        return font;
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
        _data.Content = _textBox.Markdown;
        _data.X = Left;
        _data.Y = Top;
        _data.Width = Width;
        _data.Height = Height;
        return _data;
    }

    // ---------- 顏色 ----------

    private ToolStripDropDown? _colorMenu;

    /// <summary>色盤上的一個色票：顯示的顏色、點了做什麼、目前是否選中</summary>
    private sealed record Swatch(NoteColor Color, Action Apply, Func<bool> IsSelected);

    /// <summary>
    /// 點 🎨：在按鈕下方跳出色盤。最上方切換淺色／深色，下面是該組的預設色（目前的顏色加外框標示），
    /// 最下方是自訂顏色的色相條：拖曳時便箋即時換色，任何色相都換算成適合便箋的淺色或深色。
    /// </summary>
    private void ShowColorMenu()
    {
        _colorMenu?.Dispose();

        var light = Enumerable.Range(0, Theme.LightPresetCount)
            .Select(i => new Swatch(Palette[i], () => SetColor(i), () => _data.CustomHue == null && _data.ColorIndex == i))
            .ToList();
        var dark = Enumerable.Range(0, Theme.DarkVariantCount)
            .Select(i =>
            {
                int hue = Theme.PresetHue(i);
                var color = Theme.FromHue(hue, dark: true) with { Name = "深" + Palette[i].Name };
                return new Swatch(color, () => SetCustomHue(hue, dark: true),
                    () => _data.CustomHue == hue && _data.CustomDark);
            })
            .Append(new Swatch(Palette[Theme.CharcoalIndex], () => SetColor(Theme.CharcoalIndex),
                () => _data.CustomHue == null && _data.ColorIndex == Theme.CharcoalIndex))
            .ToList();

        int size = _titleBar.Height;
        // 色盤是不取得焦點的彈出視窗，一般 ToolTip 只在視窗作用中才顯示，所以另建一個 ShowAlways 的
        var swatchTip = new ToolTip { ShowAlways = true };
        var lightRow = MakeSwatchRow(light, size, swatchTip);
        var darkRow = MakeSwatchRow(dark, size, swatchTip);
        int rowWidth = Math.Max(lightRow.GetPreferredSize(Size.Empty).Width, darkRow.GetPreferredSize(Size.Empty).Width);

        var hueBar = new HueBar
        {
            Size = new Size(rowWidth - Dpi(4), Math.Max(Dpi(14), size / 2)),
            Margin = new Padding(Dpi(2), 0, Dpi(2), Dpi(4)),
            AccessibleName = "自訂顏色",
        };
        hueBar.HueChanged += hue =>
        {
            SetCustomHue(hue, hueBar.Dark);
            lightRow.Invalidate(true); // 預設色的選取外框要拿掉
            darkRow.Invalidate(true);
        };

        // 淺色／深色切換：只換色盤顯示的那一組，選了顏色才改便箋
        var btnLight = MakeModeButton("淺色");
        var btnDark = MakeModeButton("深色");
        void ShowMode(bool isDark)
        {
            lightRow.Visible = !isDark;
            darkRow.Visible = isDark;
            hueBar.Dark = isDark;
            hueBar.Hue = _data.CustomHue != null && _data.CustomDark == isDark ? _data.CustomHue : null;
            btnLight.Checked = !isDark;
            btnDark.Checked = isDark;
        }
        btnLight.Click += (_, _) => ShowMode(false);
        btnDark.Click += (_, _) => ShowMode(true);
        var modeRow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            BackColor = Theme.MenuBack,
            Margin = new Padding(Dpi(2), Dpi(2), 0, Dpi(4)),
            Padding = Padding.Empty,
        };
        modeRow.Controls.Add(btnLight);
        modeRow.Controls.Add(btnDark);

        var customLabel = new Label
        {
            Text = "自訂顏色",
            AutoSize = true,
            ForeColor = Theme.MenuTextMuted,
            Margin = new Padding(Dpi(6), Dpi(6), 0, Dpi(2)),
        };

        var layout = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Theme.MenuBack,
            Margin = Padding.Empty,
            Padding = Padding.Empty, // 外圍留白由 MenuStyle 的選單內距負責
        };
        layout.Controls.Add(modeRow);
        layout.Controls.Add(lightRow);
        layout.Controls.Add(darkRow);
        layout.Controls.Add(customLabel);
        layout.Controls.Add(hueBar);
        ShowMode(CurrentColor.Dark);

        _colorMenu = new ToolStripDropDown { Padding = Padding.Empty };
        _colorMenu.Disposed += (_, _) => swatchTip.Dispose();
        _colorMenu.Items.Add(new ToolStripControlHost(layout) { Margin = Padding.Empty, Padding = Padding.Empty });
        _colorMenu.Closed += (_, _) => UpdateReveal();
        MenuStyle.Apply(_colorMenu);
        _colorMenu.Show(_btnColor, new Point(0, _btnColor.Height));
    }

    private FlowLayoutPanel MakeSwatchRow(List<Swatch> swatches, int size, ToolTip tip)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            BackColor = Theme.MenuBack,
            Padding = Padding.Empty,
            Margin = Padding.Empty,
        };
        foreach (var item in swatches)
        {
            var swatch = new Button
            {
                Size = new Size(size, size),
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.MenuBack,
                Margin = new Padding(2),
                TabStop = false,
                Cursor = Cursors.Hand,
                AccessibleName = item.Color.Name,
            };
            swatch.FlatAppearance.BorderSize = 0;
            swatch.FlatAppearance.MouseOverBackColor = Theme.MenuHover;
            swatch.Paint += (_, e) => PaintSwatch(e.Graphics, swatch.ClientRectangle, item.Color, item.IsSelected());
            swatch.Click += (_, _) =>
            {
                _colorMenu?.Close();
                item.Apply();
            };
            tip.SetToolTip(swatch, item.Color.Name);
            row.Controls.Add(swatch);
        }
        return row;
    }

    /// <summary>淺色／深色切換鈕：選中的那個有淡灰底</summary>
    private RadioButton MakeModeButton(string text)
    {
        var btn = new RadioButton
        {
            Text = text,
            Appearance = Appearance.Button,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.MenuBack,
            ForeColor = Theme.MenuText,
            Padding = new Padding(Dpi(6), 0, Dpi(6), 0),
            Margin = new Padding(0, 0, Dpi(2), 0),
            TabStop = false,
            Cursor = Cursors.Hand,
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.FlatAppearance.CheckedBackColor = Theme.MenuHover;
        btn.FlatAppearance.MouseOverBackColor = Theme.MenuHover;
        return btn;
    }

    /// <summary>色盤上的圓形色票：內容色填滿、標題列色描邊；選中的外加深灰色圈</summary>
    private static void PaintSwatch(Graphics g, Rectangle bounds, NoteColor color, bool selected)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int d = bounds.Height * 3 / 5;
        var rect = new Rectangle(bounds.X + (bounds.Width - d) / 2, bounds.Y + (bounds.Height - d) / 2, d, d);
        using (var brush = new SolidBrush(color.Body))
            g.FillEllipse(brush, rect);
        using (var pen = new Pen(Darken(color.Header)))
            g.DrawEllipse(pen, rect);
        if (selected)
        {
            using var ring = new Pen(Theme.SwatchRing, 2f);
            g.DrawEllipse(ring, Rectangle.Inflate(rect, 3, 3));
        }
    }

    private void SetColor(int index)
    {
        if (index == _data.ColorIndex && _data.CustomHue == null)
            return;
        _data.ColorIndex = index;
        _data.CustomHue = null;
        _data.CustomDark = false;
        ApplyColor();
        Changed?.Invoke();
    }

    private void SetCustomHue(int hue, bool dark)
    {
        if (_data.CustomHue == hue && _data.CustomDark == dark)
            return;
        _data.CustomHue = hue;
        _data.CustomDark = dark;
        // ColorIndex 是舊版的退路：深色自訂色退回炭黑，淺色則不能停在炭黑
        if (dark)
            _data.ColorIndex = Theme.CharcoalIndex;
        else if (_data.ColorIndex == Theme.CharcoalIndex)
            _data.ColorIndex = 0;
        ApplyColor();
        Changed?.Invoke();
    }

    private int Dpi(int px) => (int)Math.Round(px * DeviceDpi / 96f);

    private void ApplyColor()
    {
        var color = CurrentColor;
        BackColor = color.Body;         // Padding 邊緣也會呈現內容色
        _contentPanel.BackColor = color.Body;
        _textBox.BackColor = color.Body;
        _textBox.ForeColor = color.Text; // 深色便箋用淺色字
        _titleLabel.ForeColor = color.Text;
        _reminderLabel.ForeColor = color.TextMuted;
        _grip.DotColor = color.Outline;
        PaintHeader(color.Header);
        ApplyBorderColor();
    }

    /// <summary>標題列與按鈕上色（響鈴閃爍也用這個）</summary>
    private void PaintHeader(Color header)
    {
        _titleBar.BackColor = header;
        var color = CurrentColor;
        foreach (var b in _titleButtons)
        {
            b.BackColor = header;
            b.ForeColor = color.Glyph;
            b.HoverColor = color.Hover(header, 0.09f);
            b.PressedColor = color.Hover(header, 0.16f);
        }
        Invalidate(); // 縮放邊的色帶要跟著換色（OnPaintBackground）
    }

    /// <summary>
    /// 視窗四周留給縮放感應的邊（Padding）預設是內容色，會讓標題列看起來像一塊內縮的貼片。
    /// 把標題列與提醒列兩側（及最上方）的邊也塗成它們的顏色，色帶就延伸到視窗邊緣。
    /// </summary>
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        var title = _titleBar.Bounds;
        using (var brush = new SolidBrush(_titleBar.BackColor))
            e.Graphics.FillRectangle(brush, 0, 0, Width, title.Bottom);
        if (_reminderBar.Visible)
        {
            var bar = _reminderBar.Bounds;
            using var brush = new SolidBrush(_reminderBar.BackColor);
            e.Graphics.FillRectangle(brush, 0, bar.Top, Width, bar.Height);
        }
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        Invalidate(); // 標題列高度或提醒列顯示與否改變時，色帶位置跟著變
    }

    private static Color Darken(Color c) => Theme.Darken(c);

    private TitleButton MakeTitleButton(string glyph, string tooltip)
    {
        var btn = new TitleButton
        {
            Text = glyph,        // 圖示字型的字碼；字型由 ApplySettings 設定
            Width = 38,
            ForeColor = Theme.NoteGlyph,
            AccessibleName = tooltip,
        };
        _toolTip.SetToolTip(btn, tooltip);
        // 便箋很窄時標題列幾乎全是按鈕：在按鈕上按住移動也能拖曳視窗
        btn.MouseDown += TitleButton_MouseDown;
        btn.MouseMove += TitleButton_MouseMove;
        return btn;
    }

    private Button MakeBarButton(string text, string tooltip)
    {
        var btn = new Button
        {
            Text = text,
            Dock = DockStyle.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(4, 0, 4, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.AlertButton,
            ForeColor = Theme.AlertText,
            TabStop = false,
            Cursor = Cursors.Hand,
        };
        btn.FlatAppearance.BorderColor = Darken(Theme.Alert);
        btn.FlatAppearance.MouseOverBackColor = Theme.AlertButtonHover;
        _toolTip.SetToolTip(btn, tooltip);
        return btn;
    }

    // ---------- 標題列按鈕淡入淡出 ----------
    // 便箋是主角、按鈕是配角：便箋在使用中（有焦點、滑鼠在上面、正在響鈴、色盤開著）才顯示按鈕，
    // 其餘時間只留名稱與提醒時間。按鈕位置保留不動，淡出只是看不見，版面不會跳動。

    private const float RevealDurationMs = 150f;
    private readonly System.Windows.Forms.Timer _revealTimer = new() { Interval = 15 };
    private readonly System.Windows.Forms.Timer _hoverPoll = new() { Interval = 120 };
    private readonly System.Diagnostics.Stopwatch _revealClock = new();
    private float _reveal;      // 0 = 隱藏、1 = 完全顯示
    private bool _active;       // 便箋是前景視窗
    private bool _mouseInside;  // 滑鼠在便箋上（含縮放邊）

    private bool RevealWanted =>
        _active || _mouseInside || IsRinging || _renameBox != null || _colorMenu?.Visible == true;

    private void UpdateReveal(bool animate = true)
    {
        float target = RevealWanted ? 1f : 0f;
        if (!animate || !Visible)
        {
            _revealTimer.Stop();
            SetReveal(target);
            return;
        }
        if (_reveal != target && !_revealTimer.Enabled)
        {
            _revealClock.Restart();
            _revealTimer.Start();
        }
    }

    private void RevealTimer_Tick(object? sender, EventArgs e)
    {
        float target = RevealWanted ? 1f : 0f;
        float step = _revealClock.ElapsedMilliseconds / RevealDurationMs;
        _revealClock.Restart();
        float next = target > _reveal ? Math.Min(target, _reveal + step) : Math.Max(target, _reveal - step);
        SetReveal(next);
        if (next == target)
            _revealTimer.Stop();
    }

    private void SetReveal(float value)
    {
        _reveal = value;
        float eased = value * value * (3f - 2f * value); // smoothstep：頭尾放慢，比等速自然
        foreach (var b in _titleButtons)
            b.Reveal = eased;
    }

    /// <summary>
    /// 滑鼠進入便箋任何一個子控制項就算移入；移出改用輪詢判斷——
    /// 縮放邊是非工作區（WM_NCHITTEST），從那裡離開不會有 MouseLeave。
    /// </summary>
    private void HookHover(Control control)
    {
        control.MouseEnter += (_, _) =>
        {
            _hoverPoll.Start();
            if (_mouseInside) return;
            _mouseInside = true;
            UpdateReveal();
        };
        control.ControlAdded += (_, e) => { if (e.Control != null) HookHover(e.Control); };
        foreach (Control child in control.Controls)
            HookHover(child);
    }

    private void HoverPoll_Tick(object? sender, EventArgs e)
    {
        if (Visible && IsCursorOverNote())
            return;
        _hoverPoll.Stop();
        _mouseInside = false;
        UpdateReveal();
    }

    /// <summary>游標在便箋範圍內、而且沒有被其他視窗蓋住</summary>
    private bool IsCursorOverNote()
    {
        var pos = Cursor.Position;
        if (!Bounds.Contains(pos))
            return false;
        return GetAncestor(WindowFromPoint(pos), GA_ROOT) == Handle;
    }

    private const uint GA_ROOT = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        _active = true;
        UpdateReveal();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        _active = false;
        UpdateReveal();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        UpdateReveal(animate: false);
    }

    // ---------- 提醒 ----------

    /// <summary>提醒時間到、正在等使用者按「延後」或「完成」</summary>
    public bool IsRinging { get; private set; }

    private void BtnReminder_Click(object? sender, EventArgs e)
    {
        using var dlg = new ReminderForm(ToData(), CurrentColor.Accent); // ToData：讓預覽拿到最新內容
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        _data.ReminderAt = dlg.Cleared ? null : dlg.ReminderAt;
        var rule = dlg.Cleared ? RepeatRule.None : dlg.Rule;
        _data.ReminderRepeat = rule.Kind;
        _data.ReminderWeekDays = rule.WeekDays;
        _data.ReminderMonthDay = rule.MonthDay;
        _data.ReminderSnoozeUntil = null; // 重新設定 = 放棄之前的延後
        StopRinging();
        RefreshReminderLabel();
        Changed?.Invoke();
    }

    /// <summary>更新標題列的提醒時間。跨日後「明天」要變成當天時間，所以管理端會定時呼叫</summary>
    public void RefreshReminderLabel()
    {
        var due = ReminderSchedule.DueTime(_data);
        if (due == null)
        {
            _reminderFull = _reminderCompact = string.Empty;
            _toolTip.SetToolTip(_reminderLabel, null);
            FitReminderLabel();
            return;
        }

        string repeatMark = _data.ReminderRepeat == ReminderRepeat.None ? "" : " ↻";
        _reminderFull = $"{ReminderSchedule.FormatShort(due.Value, DateTime.Now)}{repeatMark}";
        _reminderCompact = $"{due.Value:HH:mm}";
        FitReminderLabel();

        string tip = $"提醒：{due.Value:yyyy/MM/dd HH:mm}（{ReminderSchedule.Describe(RepeatRule.From(_data))}）";
        if (_data.ReminderSnoozeUntil != null) tip += "，已延後";
        _toolTip.SetToolTip(_reminderLabel, tip);
    }

    /// <summary>
    /// 響鈴：不搶鍵盤焦點地把便箋帶到最上層，標題列閃爍、播放提示音、顯示提醒列。
    /// 提醒列會一直留著，直到使用者按「延後」或「完成」。
    /// </summary>
    public void StartRinging()
    {
        if (IsRinging || ReminderSchedule.DueTime(_data) is not { } due)
            return;
        IsRinging = true;

        var now = DateTime.Now;
        bool overdue = now - due > TimeSpan.FromMinutes(2); // 程式沒在執行或電腦睡眠時錯過的
        _reminderBarLabel.Text = overdue
            ? $"已逾時（原定 {ReminderSchedule.FormatShort(due, now)}）"
            : "提醒時間到了";
        _toolTip.SetToolTip(_reminderBarLabel, _reminderBarLabel.Text);
        _reminderBar.Visible = true;
        UpdateReveal();

        ShowWithoutFocus();
        // 響鈴期間暫時置頂確保看得到；StopRinging 時依設定還原
        SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

        System.Media.SystemSounds.Exclamation.Play();
        _flashCount = 0;
        _flashTimer.Start();
    }

    /// <summary>延後 10 分鐘：只記延後時間，不動原定時間（重複提醒的基準）</summary>
    private void SnoozeReminder()
    {
        _data.ReminderSnoozeUntil = DateTime.Now.AddMinutes(10);
        StopRinging();
        RefreshReminderLabel();
        Changed?.Invoke();
    }

    /// <summary>完成：不重複的提醒清除；重複的提醒從原定時間往後排到下一次</summary>
    public void CompleteReminder()
    {
        if (_data.ReminderAt is { } at && _data.ReminderRepeat != ReminderRepeat.None)
            _data.ReminderAt = ReminderSchedule.Next(at, RepeatRule.From(_data), DateTime.Now);
        else
            _data.ReminderAt = null;
        _data.ReminderSnoozeUntil = null;
        StopRinging();
        RefreshReminderLabel();
        Changed?.Invoke();
    }

    private void StopRinging()
    {
        if (!IsRinging)
            return;
        IsRinging = false;
        _flashTimer.Stop();
        _reminderBar.Visible = false;
        ApplyColor();
        UpdateReveal();
        if (!_settings.AlwaysOnTop)
            SetWindowPos(Handle, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>標題列閃 10 秒後恢復原色；提醒列繼續顯示</summary>
    private void FlashTimer_Tick(object? sender, EventArgs e)
    {
        _flashCount++;
        var header = CurrentColor.Header;
        if (_flashCount >= 20)
        {
            _flashTimer.Stop();
            PaintHeader(header);
            return;
        }
        PaintHeader(_flashCount % 2 == 1 ? Theme.Alert : header);
    }

    // ---------- 名稱 ----------

    private void RefreshTitle()
    {
        _titleLabel.Text = _data.Title;
        _toolTip.SetToolTip(_titleLabel, _data.Title.Length == 0 ? "雙擊命名" : $"{_data.Title}（雙擊改名）");
    }

    /// <summary>
    /// 提醒時間優先完整顯示：寬度取「文字需要的寬度」與「扣掉按鈕後剩下的寬度」較小者，
    /// 名稱（Fill）用剩下的空間，不夠就以「…」截斷。
    /// </summary>
    private string _reminderFull = string.Empty;    // 「明天 09:00 ↻」（鈴鐺圖示由標籤自己畫）
    private string _reminderCompact = string.Empty; // 空間不夠時的「09:00」

    private int ButtonsWidth =>
        _btnColor.Width + _btnDelete.Width + _btnNew.Width + _btnReminder.Width + _btnClose.Width;

    private int MeasureReminder(string text) =>
        text.Length == 0 ? 0 : _reminderLabel.MeasureContent(text) + 6;

    private void FitReminderLabel()
    {
        int available = Math.Max(0, _titleBar.ClientSize.Width - ButtonsWidth);
        int full = MeasureReminder(_reminderFull);
        bool useFull = full <= available;
        _reminderLabel.Text = useFull ? _reminderFull : _reminderCompact;
        _reminderLabel.Width = Math.Min(useFull ? full : MeasureReminder(_reminderCompact), available);
        UpdateMinimumSize();
    }

    /// <summary>
    /// 最小寬度依實際按鈕寬度計算（按鈕寬度會隨字級與 DPI 變大，寫死會讓按鈕互相擠壓、蓋掉左側按鈕）：
    /// 5 顆按鈕 + 左右縮放邊 + 一點留白；有提醒時再加上短格式時間的寬度，最窄也看得到幾點提醒。
    /// </summary>
    private void UpdateMinimumSize()
    {
        int width = ButtonsWidth + Padding.Horizontal + 8 + MeasureReminder(_reminderCompact);
        if (MinimumSize.Width != width)
            MinimumSize = new Size(width, 120);
    }

    /// <summary>雙擊名稱：就地顯示輸入框。Enter／離開輸入框 = 確定，Esc = 取消</summary>
    private void BeginRename()
    {
        if (_renameBox != null)
            return;

        var box = new TextBox
        {
            Text = _data.Title,
            MaxLength = 40,
            Font = _titleLabel.Font,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "便箋名稱",
        };
        // 疊在名稱的位置；名稱區太窄時至少給 120px，暫時蓋住右側按鈕也沒關係
        var r = _titleLabel.Bounds;
        int width = Math.Max(r.Width, 120);
        box.SetBounds(r.X, r.Y + (r.Height - box.PreferredHeight) / 2, Math.Min(width, _titleBar.ClientSize.Width - r.X), box.PreferredHeight);

        bool done = false;
        void Finish(bool commit)
        {
            if (done) return;
            done = true;
            if (commit)
            {
                string title = box.Text.Trim();
                if (title != _data.Title)
                {
                    _data.Title = title;
                    Changed?.Invoke();
                }
                RefreshTitle();
            }
            _renameBox = null;
            UpdateReveal();
            // 移除與釋放延到事件處理結束後：不能在輸入框自己的 KeyDown/LostFocus 裡把它 Dispose 掉
            BeginInvoke(() =>
            {
                _titleBar.Controls.Remove(box);
                box.Dispose();
                _textBox.Focus();
            });
        }
        box.KeyDown += (_, e) =>
        {
            if (e.KeyCode is Keys.Enter or Keys.Escape)
            {
                e.SuppressKeyPress = true; // 不要系統提示音
                Finish(commit: e.KeyCode == Keys.Enter);
            }
        };
        box.LostFocus += (_, _) => Finish(commit: true);

        _renameBox = box;
        UpdateReveal();
        _titleBar.Controls.Add(box);
        box.BringToFront();
        box.Focus();
        box.SelectAll();
    }

    // ---------- 不搶焦點的顯示 ----------

    /// <summary>
    /// 浮到所有一般視窗的上方，但不搶焦點：先設為置頂再取消置頂，
    /// 視窗會停在「非置頂視窗」的最上層。設定為置頂或正在響鈴時保持置頂。
    /// </summary>
    public void RaiseToTop()
    {
        SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        if (!_settings.AlwaysOnTop && !IsRinging)
            SetWindowPos(Handle, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    protected override bool ShowWithoutActivation => _showQuietly;

    /// <summary>
    /// 顯示便箋但不搶鍵盤焦點：響鈴時使用者可能正在別的程式打字，
    /// 一般的 Show() 會把焦點搶走，讓字打進便箋裡。
    /// </summary>
    public void ShowWithoutFocus()
    {
        _showQuietly = true;
        try
        {
            if (!Visible)
                Show();
            if (WindowState == FormWindowState.Minimized)
                ShowWindow(Handle, SW_SHOWNOACTIVATE); // 還原最小化但不啟用
        }
        finally
        {
            _showQuietly = false;
        }
    }

    private const int SW_SHOWNOACTIVATE = 4;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new(-2);
    private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    // ---------- 字型大小（Ctrl+滾輪） ----------

    private void TextBox_MouseWheel(object? sender, MouseEventArgs e)
    {
        if (ModifierKeys != Keys.Control) return;
        if (e is HandledMouseEventArgs h) h.Handled = true; // 不要同時捲動

        float size = Math.Clamp(_data.FontSize + (e.Delta > 0 ? 1f : -1f), 8f, 32f);
        if (Math.Abs(size - _data.FontSize) < 0.1f) return;

        _data.FontSize = size;
        var old = _textBox.Font;
        _textBox.Font = OwnFont(_settings.ContentFontFamily, size);
        _ownedFonts.Remove(old);
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

    // ---------- 圓角、陰影、邊框（Windows 11 原生 DWM） ----------

    private const int DWMWA_NCRENDERING_POLICY = 2;
    private const int DWMNCRP_ENABLED = 2;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;
    private const int DWMWA_BORDER_COLOR = 34;

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS { public int Left, Right, Top, Bottom; }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            int pref = DWMWCP_ROUND;
            DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));

            // 無邊框視窗預設沒有陰影，看起來像平貼在桌面上的色塊。
            // 開啟 DWM 非工作區繪製並把框架延伸 1px，DWM 就會替它畫原生陰影（浮在桌面上的紙）
            int policy = DWMNCRP_ENABLED;
            DwmSetWindowAttribute(Handle, DWMWA_NCRENDERING_POLICY, ref policy, sizeof(int));
            var margins = new MARGINS { Left = 1, Right = 1, Top = 1, Bottom = 1 };
            DwmExtendFrameIntoClientArea(Handle, ref margins);
        }
        catch
        {
            // Windows 10 以下沒有這些 API：維持直角、無陰影，不影響功能
        }
        ApplyBorderColor();
    }

    /// <summary>Windows 11 的視窗外框改用便箋同色系的深色，取代系統預設的灰框，在深色桌布上也有清楚輪廓</summary>
    private void ApplyBorderColor()
    {
        if (!IsHandleCreated)
            return;
        var c = CurrentColor.Outline;
        int colorRef = c.R | (c.G << 8) | (c.B << 16);
        try { DwmSetWindowAttribute(Handle, DWMWA_BORDER_COLOR, ref colorRef, sizeof(int)); }
        catch { /* Windows 10 沒有此屬性 */ }
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
            StartWindowDrag();
    }

    /// <summary>交給系統做原生視窗拖曳：流暢且支援貼齊</summary>
    private void StartWindowDrag()
    {
        ReleaseCapture();
        SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
    }

    private Point _buttonPressPoint; // 在標題列按鈕上按下時的游標位置（螢幕座標）

    private void TitleButton_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
            _buttonPressPoint = Cursor.Position;
    }

    /// <summary>
    /// 按住按鈕移動超過系統拖曳門檻 → 改為拖曳視窗，這次按下不算點擊；
    /// 沒移動就放開則照常觸發按鈕。跟桌面圖示「點一下開啟、按住拖動移動」同一個邏輯。
    /// </summary>
    private void TitleButton_MouseMove(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || sender is not Control control)
            return;

        var pos = Cursor.Position;
        var threshold = SystemInformation.DragSize;
        if (Math.Abs(pos.X - _buttonPressPoint.X) < threshold.Width &&
            Math.Abs(pos.Y - _buttonPressPoint.Y) < threshold.Height)
            return;

        StartWindowDrag(); // 拖曳結束（放開滑鼠）後才返回；放開事件被系統吃掉，不會觸發 Click
        control.Invalidate(); // 清掉按鈕按下狀態殘留的顏色
    }

    // ---------- 邊緣縮放（WM_NCHITTEST） ----------

    /// <summary>
    /// 單行標籤：寬度不夠時以「…」截斷。內建 Label 在寬度不足時會自動換行，
    /// 只露出第一行的一兩個字、且整段垂直置中後第一行偏上，在標題列上很難看。
    /// </summary>
    private sealed class SingleLineLabel : Label
    {
        private const TextFormatFlags BaseFlags = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

        /// <summary>畫在文字前面的圖示（圖示字型的字碼）；有文字時才畫</summary>
        public string Icon { get; set; } = string.Empty;

        public Font? IconFont { get; set; }

        private bool HasIcon => Icon.Length > 0 && IconFont != null;
        private int IconGap => Font.Height / 4;

        private int IconWidth =>
            TextRenderer.MeasureText(Icon, IconFont, Size.Empty, TextFormatFlags.NoPadding).Width;

        /// <summary>圖示 + 文字需要的寬度</summary>
        public int MeasureContent(string text)
        {
            int width = TextRenderer.MeasureText(text, Font, Size.Empty, TextFormatFlags.NoPadding).Width;
            return HasIcon ? width + IconWidth + IconGap : width;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var rect = new Rectangle(Padding.Left, Padding.Top,
                Width - Padding.Horizontal, Height - Padding.Vertical);
            var align = TextAlign switch
            {
                ContentAlignment.MiddleRight => TextFormatFlags.Right,
                ContentAlignment.MiddleCenter => TextFormatFlags.HorizontalCenter,
                _ => TextFormatFlags.Left,
            };

            if (HasIcon && Text.Length > 0)
            {
                // 圖示 + 文字當成一組對齊；靠右時寬度不夠就從左邊開始、文字以「…」截斷
                int iconWidth = IconWidth;
                int total = Math.Min(MeasureContent(Text), rect.Width);
                int x = align == TextFormatFlags.Right ? rect.Right - total
                      : align == TextFormatFlags.HorizontalCenter ? rect.X + (rect.Width - total) / 2
                      : rect.X;
                TextRenderer.DrawText(e.Graphics, Icon, IconFont, new Rectangle(x, rect.Y, iconWidth, rect.Height),
                    ForeColor, TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                int textX = x + iconWidth + IconGap;
                rect = new Rectangle(textX, rect.Y, Math.Max(0, rect.Right - textX), rect.Height);
                align = TextFormatFlags.Left;
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, rect, ForeColor, BaseFlags | align);
        }
    }

    /// <summary>
    /// 自訂顏色的色相條：由左到右是 0–359 度色相（都已換算成便箋用的淡色），
    /// 按下或拖曳就選色；目前的自訂色以圓形標記標出，用預設色時不顯示標記。
    /// </summary>
    private sealed class HueBar : Control
    {
        private static Color[]? s_light, s_dark; // 360 個色相的內容色，所有便箋共用
        private int? _hue;
        private bool _dark;

        public event Action<int>? HueChanged;

        public HueBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.Slider;
        }

        public int? Hue
        {
            get => _hue;
            set { _hue = value; Invalidate(); }
        }

        /// <summary>true = 顯示深色版的色相</summary>
        public bool Dark
        {
            get => _dark;
            set { _dark = value; Invalidate(); }
        }

        private Color[] Colors => _dark
            ? s_dark ??= Enumerable.Range(0, 360).Select(h => Theme.FromHue(h, dark: true).Body).ToArray()
            : s_light ??= Enumerable.Range(0, 360).Select(h => Theme.FromHue(h).Body).ToArray();

        /// <summary>色條左右各縮半個標記寬，標記移到兩端也不會被切掉</summary>
        private Rectangle BarRect
        {
            get
            {
                int r = Height / 2;
                return new Rectangle(r, Height / 4, Math.Max(1, Width - r * 2), Height / 2);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.MenuBack);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var bar = BarRect;
            var colors = Colors;

            using (var path = Theme.RoundedRect(bar, bar.Height / 2))
            {
                var clip = g.Clip;
                g.SetClip(path);
                for (int x = 0; x < bar.Width; x++)
                {
                    using var pen = new Pen(colors[x * 360 / bar.Width]);
                    g.DrawLine(pen, bar.X + x, bar.Y, bar.X + x, bar.Bottom);
                }
                g.Clip = clip;
                using var outline = new Pen(Theme.MenuSeparator);
                g.DrawPath(outline, path);
            }

            if (_hue is int hue)
            {
                int cx = bar.X + hue * bar.Width / 360;
                int d = Height - 2;
                var marker = new Rectangle(cx - d / 2, (Height - d) / 2, d, d);
                using (var fill = new SolidBrush(colors[hue]))
                    g.FillEllipse(fill, marker);
                using (var white = new Pen(Color.White, Math.Max(2f, d / 8f)))
                    g.DrawEllipse(white, Rectangle.Inflate(marker, -(int)(d / 10f), -(int)(d / 10f)));
                using (var ring = new Pen(Theme.SwatchRing, 1.5f))
                    g.DrawEllipse(ring, marker);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
                Pick(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (e.Button == MouseButtons.Left)
                Pick(e.X);
        }

        private void Pick(int x)
        {
            var bar = BarRect;
            int hue = Math.Clamp((x - bar.X) * 360 / bar.Width, 0, 359);
            if (hue == _hue)
                return;
            Hue = hue;
            HueChanged?.Invoke(hue);
        }
    }

    /// <summary>
    /// 標題列按鈕：自繪圖示字型，hover／按下時畫圓角底色（不是整格方塊）。
    /// Reveal 控制圖示濃淡：0 時圖示與標題列同色（看不見），1 時完整顯示。
    /// </summary>
    private sealed class TitleButton : Control
    {
        private bool _hover;
        private bool _pressed;
        private float _reveal = 1f;

        public TitleButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            // 不吃雙擊：連按兩下 ＋ 應該是兩次 Click，而不是一次 Click 加一次 DoubleClick
            SetStyle(ControlStyles.Selectable | ControlStyles.StandardDoubleClick, false);
            TabStop = false;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.PushButton;
        }

        public Color HoverColor { get; set; } = SystemColors.ControlLight;
        public Color PressedColor { get; set; } = SystemColors.ControlDark;

        public float Reveal
        {
            get => _reveal;
            set
            {
                if (_reveal == value) return;
                _reveal = value;
                Invalidate();
            }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = _pressed = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        /// <summary>按住拖曳視窗時 capture 被系統拿走、收不到 MouseUp：在這裡清掉按下狀態</summary>
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            _pressed = false;
            _hover = IsHandleCreated && ClientRectangle.Contains(PointToClient(Cursor.Position));
            Invalidate();
            base.OnMouseCaptureChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            if (Text.Length == 0)
                return;

            if ((_hover || _pressed) && _reveal > 0f)
            {
                int inset = Math.Max(2, Height / 8);
                var rect = Rectangle.Inflate(ClientRectangle, -inset, -inset);
                var fill = _pressed ? PressedColor : HoverColor;
                using var path = Theme.RoundedRect(rect, Math.Max(3, Height / 6));
                using var brush = new SolidBrush(Theme.Blend(BackColor, fill, _reveal));
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillPath(brush, path);
            }

            var glyph = Theme.Blend(BackColor, ForeColor, _reveal);
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, glyph,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>
    /// 右下角縮放提示：畫一個由小點排成的三角形（跟 Windows Sticky Notes 類似）。
    /// 背景透明（顯示便箋底色），游標為斜向縮放；實際縮放由便箋處理 MouseDown。
    /// </summary>
    private sealed class ResizeGrip : Control
    {
        private Color _dotColor = Color.Gray;

        public ResizeGrip()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.SizeNWSE;
            TabStop = false;
        }

        public Color DotColor
        {
            get => _dotColor;
            set { _dotColor = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // 3 層點陣：右下角 3 點、往左上 2 點、最外 1 點，排成直角在右下的三角形
            int dot = Math.Max(2, Width / 8);
            int step = Width / 3;
            using var brush = new SolidBrush(_dotColor);
            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 3; col++)
                {
                    if (row + col < 2) continue; // 只畫右下三角
                    int x = col * step + (step - dot) / 2;
                    int y = row * step + (step - dot) / 2;
                    e.Graphics.FillRectangle(brush, x, y, dot, dot);
                }
            }
        }
    }

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
        // 使用者主動關閉（如 Alt+F4）視同按「✕」＝關閉並保留內容，統一交由管理端處理；
        // 程式結束（ApplicationExitCall）則直接放行
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            CloseRequested?.Invoke(this);
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            // 控制項已隨表單釋放，這時才能安全釋放它們使用的字型
            _toolTip.Dispose();
            _colorMenu?.Dispose();
            _flashTimer.Dispose();
            _revealTimer.Dispose();
            _hoverPoll.Dispose();
            foreach (var f in _ownedFonts)
                f.Dispose();
            _ownedFonts.Clear();
        }
    }

    // ---------- 工具 ----------

    /// <summary>
    /// 確保還原的視窗抓得回來（例如拔掉外接螢幕後）：
    /// 標題列有一段落在某個螢幕的工作區內就維持原位（允許跨螢幕擺放）；
    /// 否則移進最近的螢幕。不用 VirtualScreen 外框判斷，因為螢幕排成 L 形
    /// 或大小不一時，外框內仍有不屬於任何螢幕的死角。
    /// </summary>
    private static Rectangle ClampToScreen(Rectangle r)
    {
        if (r.Width < 160) r.Width = 640;
        if (r.Height < 120) r.Height = 480;

        var titleStrip = new Rectangle(r.X, r.Y, r.Width, TitleHeight);
        bool reachable = Screen.AllScreens.Any(s =>
        {
            var visible = Rectangle.Intersect(s.WorkingArea, titleStrip);
            return visible.Width >= 60 && visible.Height >= 10; // 至少露出一段可拖曳的標題列
        });
        if (reachable)
            return r;

        var wa = Screen.FromRectangle(r).WorkingArea;
        r.Width = Math.Min(r.Width, wa.Width);
        r.Height = Math.Min(r.Height, wa.Height);
        r.X = Math.Max(wa.Left, Math.Min(r.X, wa.Right - r.Width));
        r.Y = Math.Max(wa.Top, Math.Min(r.Y, wa.Bottom - r.Height));
        return r;
    }
}
