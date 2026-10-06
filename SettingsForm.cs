namespace MemoTack;

/// <summary>
/// 設定視窗：外觀（字型、字級、置頂）、全域快捷鍵、登入自動啟動。
/// 按「確定」時把值寫回傳入的 AppSettings。
/// </summary>
public class SettingsForm : StyledDialog
{
    private readonly AppSettings _settings;
    private readonly ComboBox _titleFont;
    private readonly NumericUpDown _titleSize;
    private readonly ComboBox _contentFont;
    private readonly NumericUpDown _contentSize;
    private readonly CheckBox _alwaysOnTop;
    private readonly CheckBox _autoStart;
    private readonly TextBox _hotkeyBox;
    private readonly TextBox _restoreHotkeyBox;
    private readonly ToolTip _toolTip = new();

    public SettingsForm(AppSettings settings) : base("MemoTack 設定")
    {
        _settings = settings;
        StartPosition = FormStartPosition.CenterScreen;

        // ---- 系統已安裝字型清單 ----
        string[] families = FontFamily.Families.Select(f => f.Name).OrderBy(n => n).ToArray();

        _titleFont = MakeFontCombo(families, settings.TitleFontFamily);
        _titleSize = MakeSizeUpDown(settings.TitleFontSize);
        _contentFont = MakeFontCombo(families, settings.ContentFontFamily);
        _contentSize = MakeSizeUpDown(settings.ContentFontSize);

        _alwaysOnTop = MakeCheck("便箋顯示在最上層（置頂）", settings.AlwaysOnTop);
        _autoStart = MakeCheck("登入 Windows 時自動啟動", StartupManager.IsEnabled()); // 以登錄實際狀態為準

        _hotkeyBox = MakeHotkeyBox(settings.Hotkey);
        _toolTip.SetToolTip(_hotkeyBox, "正在用其他程式時叫出便箋；正在操作便箋時隱藏。");
        _restoreHotkeyBox = MakeHotkeyBox(settings.RestoreHotkey);
        _toolTip.SetToolTip(_restoreHotkeyBox, "一次還原所有已關閉的便箋。");

        // ---- 內容：三個區塊 ----
        var body = MakeBody();

        AddSection(body, "外觀", first: true);
        AddRow(body, "標題列", FontRow(_titleFont, _titleSize));
        AddRow(body, "內容（預設）", FontRow(_contentFont, _contentSize));
        AddWide(body, MakeHint("新便箋使用此大小；個別便箋可用 Ctrl+滾輪 調整。\n修改此值會套用到所有便箋。"));
        AddWide(body, _alwaysOnTop);

        AddSection(body, "快捷鍵", first: false);
        AddRow(body, "叫出／隱藏便箋", _hotkeyBox);
        AddRow(body, "還原已關閉便箋", _restoreHotkeyBox);
        AddWide(body, MakeHint("點一下欄位後直接按下組合鍵；Backspace 清除＝停用。\nF12 被 Windows 保留，被其他程式占用的組合會註冊失敗。"));

        AddSection(body, "啟動", first: false);
        AddWide(body, _autoStart);

        // ---- 確定 / 取消 ----
        var btnOk = MakeButton("確定", primary: true);
        btnOk.DialogResult = DialogResult.OK;
        btnOk.Click += (_, _) => ApplyToSettings();
        var btnCancel = MakeButton("取消", primary: false);
        btnCancel.DialogResult = DialogResult.Cancel;

        SetLayout(MakeHeader("⚙  MemoTack 設定", "版本 " + VersionText(), NoteForm.Palette[0].Header),
                  body, MakeFooter(null, btnOk, btnCancel));

        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }

    /// <summary>把 UI 上的值寫回 AppSettings（按「確定」時呼叫）</summary>
    private void ApplyToSettings()
    {
        _settings.TitleFontFamily = _titleFont.Text;
        _settings.TitleFontSize = (float)_titleSize.Value;
        _settings.ContentFontFamily = _contentFont.Text;
        _settings.ContentFontSize = (float)_contentSize.Value;
        _settings.AlwaysOnTop = _alwaysOnTop.Checked;
        _settings.Hotkey = _hotkeyBox.Text.Trim();
        _settings.RestoreHotkey = _restoreHotkeyBox.Text.Trim();
        StartupManager.SetEnabled(_autoStart.Checked); // 直接寫入/移除登錄值
    }

    /// <summary>x.y.z（不含 .NET 附加的 +commit 雜湊）</summary>
    private static string VersionText()
    {
        var v = typeof(SettingsForm).Assembly.GetName().Version;
        return v == null ? "?" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _toolTip.Dispose();
    }

    // ---------- 版面 ----------

    /// <summary>區塊標題（橫跨兩欄）；非第一個區塊上方多留空白做分隔</summary>
    private void AddSection(TableLayoutPanel table, string title, bool first)
    {
        var label = new Label
        {
            Text = title,
            AutoSize = true,
            Font = OwnFont(10.5f, FontStyle.Bold),
            ForeColor = TextStrong,
            Margin = new Padding(0, first ? 0 : Dpi(16), 0, Dpi(4)),
        };
        int row = table.RowCount++;
        table.Controls.Add(label, 0, row);
        table.SetColumnSpan(label, 2);
    }

    /// <summary>放在右欄、與其他控制項對齊的一列（勾選框、說明文字）</summary>
    private static void AddWide(TableLayoutPanel table, Control control)
    {
        table.Controls.Add(control, 1, table.RowCount++);
    }

    /// <summary>字型下拉 + 字級 + 「pt」排成一列</summary>
    private FlowLayoutPanel FontRow(ComboBox font, NumericUpDown size)
    {
        var row = MakeFlow();
        font.Margin = new Padding(0, 0, Dpi(8), 0);
        size.Margin = new Padding(0, 0, Dpi(4), 0);
        row.Controls.Add(font);
        row.Controls.Add(size);
        row.Controls.Add(new Label { Text = "pt", AutoSize = true, ForeColor = TextMuted, Anchor = AnchorStyles.Left, Margin = Padding.Empty });
        return row;
    }

    // ---------- 控制項工廠 ----------

    private ComboBox MakeFontCombo(string[] families, string current)
    {
        var cb = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList, // 只能從清單選，避免打錯字型名
            Width = TextRenderer.MeasureText("Microsoft JhengHei UI Light", Font).Width + Dpi(28),
        };
        cb.Items.AddRange(families);
        cb.SelectedItem = families.Contains(current) ? current : "Segoe UI";
        if (cb.SelectedIndex < 0 && cb.Items.Count > 0)
            cb.SelectedIndex = 0;
        return cb;
    }

    private NumericUpDown MakeSizeUpDown(float current) => new()
    {
        Minimum = 7,
        Maximum = 48,
        DecimalPlaces = 0,
        Increment = 1,
        Value = Math.Clamp((decimal)current, 7, 48),
        Width = TextRenderer.MeasureText("48", Font).Width + Dpi(36),
    };

    /// <summary>灰色小字說明</summary>
    private Label MakeHint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = TextMuted,
        Font = OwnFont(9f),
        Margin = new Padding(0, Dpi(2), 0, 0),
    };

    private CheckBox MakeCheck(string text, bool isChecked) => new()
    {
        Text = text,
        Checked = isChecked,
        AutoSize = true,
        Cursor = Cursors.Hand,
        Margin = new Padding(0, Dpi(6), 0, Dpi(2)),
    };

    private HotkeyBox MakeHotkeyBox(string hotkey) => new()
    {
        Text = hotkey,
        PlaceholderText = "點此按下組合鍵",
        BorderStyle = BorderStyle.FixedSingle,
        Width = TextRenderer.MeasureText("Ctrl+Alt+Shift+F11", Font).Width + Dpi(24),
        Margin = new Padding(0, Dpi(4), 0, Dpi(4)),
    };

    /// <summary>
    /// 快捷鍵擷取框：不用打字，直接按下組合鍵就填入。
    /// Backspace / Esc / Delete 清空（＝停用）。
    /// </summary>
    private sealed class HotkeyBox : TextBox
    {
        private bool _winDown; // Win 鍵不在 e.Modifiers 裡，自己追蹤按住狀態

        public HotkeyBox()
        {
            ReadOnly = true;              // 擋一般文字輸入，但仍收得到 KeyDown
            BackColor = SystemColors.Window;
            ShortcutsEnabled = false;     // 停用右鍵貼上等
            Cursor = Cursors.Hand;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;

            if (e.KeyCode is Keys.LWin or Keys.RWin)
            {
                _winDown = true;
                return;
            }

            // 無修飾鍵時的 Esc / Backspace / Delete = 清空停用
            if (e.Modifiers == Keys.None && !_winDown &&
                e.KeyCode is Keys.Escape or Keys.Back or Keys.Delete)
            {
                Text = string.Empty;
                return;
            }

            // 只按了修飾鍵本身：等主鍵
            if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu)
                return;

            var parts = new List<string>();
            if (e.Control) parts.Add("Ctrl");
            if (e.Alt) parts.Add("Alt");
            if (e.Shift) parts.Add("Shift");
            if (_winDown) parts.Add("Win");
            if (parts.Count == 0)
                return; // 全域快捷鍵至少要一個修飾鍵

            string? keyName = KeyToString(e.KeyCode);
            if (keyName == null)
                return; // 不支援的主鍵

            Text = string.Join("+", parts) + "+" + keyName;
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode is Keys.LWin or Keys.RWin)
                _winDown = false;
            base.OnKeyUp(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            _winDown = false;
            base.OnLostFocus(e);
        }

        /// <summary>支援 A-Z、0-9、F1-F11（F12 被 Windows 保留給除錯器，不可用）</summary>
        private static string? KeyToString(Keys k)
        {
            if (k >= Keys.A && k <= Keys.Z) return k.ToString();
            if (k >= Keys.D0 && k <= Keys.D9) return ((char)('0' + (k - Keys.D0))).ToString();
            if (k >= Keys.F1 && k <= Keys.F11) return k.ToString();
            return null;
        }
    }
}
