namespace MemoTack;

/// <summary>
/// 設定視窗：標題列字型/大小、內容字型/大小。
/// 按「確定」時把值寫回傳入的 AppSettings。
/// </summary>
public class SettingsForm : Form
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

    public SettingsForm(AppSettings settings)
    {
        _settings = settings;

        // ---- 視窗基本設定 ----
        Text = "MemoTack 設定";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true; // 便箋都是置頂，設定視窗也要置頂才不會被蓋住
        Font = new Font("Segoe UI", 9f);

        // 依 DPI 縮放視窗與控制項，避免高 DPI 下文字被截斷
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        ClientSize = new Size(400, 356);

        // ---- 系統已安裝字型清單 ----
        string[] families = FontFamily.Families.Select(f => f.Name).OrderBy(n => n).ToArray();

        _titleFont = MakeFontCombo(families, settings.TitleFontFamily);
        _titleSize = MakeSizeUpDown(settings.TitleFontSize);
        _contentFont = MakeFontCombo(families, settings.ContentFontFamily);
        _contentSize = MakeSizeUpDown(settings.ContentFontSize);

        // ---- 表格佈局 ----
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14, 12, 14, 0),
            ColumnCount = 2,
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _alwaysOnTop = new CheckBox
        {
            Text = "便箋顯示在最上層（置頂）",
            Checked = settings.AlwaysOnTop,
            AutoSize = true,
        };

        _hotkeyBox = new HotkeyBox
        {
            Text = settings.Hotkey,
            Dock = DockStyle.Fill,
            PlaceholderText = "點此按下組合鍵",
        };
        new ToolTip().SetToolTip(_hotkeyBox,
            "點一下欄位，直接按下想要的組合鍵（如 Alt+F12）。\nBackspace 或 Esc 清空＝停用。\n被系統占用的組合（如 Win+S）會註冊失敗。");

        _restoreHotkeyBox = new HotkeyBox
        {
            Text = settings.RestoreHotkey,
            Dock = DockStyle.Fill,
            PlaceholderText = "點此按下組合鍵",
        };
        new ToolTip().SetToolTip(_restoreHotkeyBox, "一次還原所有已關閉的便箋。");

        AddRow(table, "標題列字型", _titleFont);
        AddRow(table, "標題列大小 (pt)", _titleSize);
        AddRow(table, "內容字型", _contentFont);
        AddRow(table, "內容大小 (pt)", _contentSize);
        AddRow(table, "顯示/隱藏快捷鍵", _hotkeyBox);
        AddRow(table, "還原已關閉快捷鍵", _restoreHotkeyBox);

        // 勾選選項橫跨兩欄
        AddCheckRow(table, _alwaysOnTop);

        _autoStart = new CheckBox
        {
            Text = "登入 Windows 時自動啟動",
            Checked = StartupManager.IsEnabled(), // 以登錄實際狀態為準
            AutoSize = true,
        };
        AddCheckRow(table, _autoStart);

        // ---- 確定 / 取消 ----
        var btnOk = new Button { Text = "確定", Width = 84, Height = 28, DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "取消", Width = 84, Height = 28, DialogResult = DialogResult.Cancel };
        btnOk.Click += (_, _) => ApplyToSettings();

        var btnPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft, // 先加入的靠最右
            Dock = DockStyle.Bottom,
            Height = 46,
            Padding = new Padding(10, 8, 10, 8),
        };
        btnPanel.Controls.Add(btnCancel);
        btnPanel.Controls.Add(btnOk);

        Controls.Add(table);
        Controls.Add(btnPanel);
        table.BringToFront();

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

    // ---------- 控制項工廠 ----------

    private static ComboBox MakeFontCombo(string[] families, string current)
    {
        var cb = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList, // 只能從清單選，避免打錯字型名
            Dock = DockStyle.Fill,
        };
        cb.Items.AddRange(families);
        cb.SelectedItem = families.Contains(current) ? current : "Segoe UI";
        if (cb.SelectedIndex < 0 && cb.Items.Count > 0)
            cb.SelectedIndex = 0;
        return cb;
    }

    private static NumericUpDown MakeSizeUpDown(float current)
    {
        return new NumericUpDown
        {
            Minimum = 7,
            Maximum = 48,
            DecimalPlaces = 0,
            Increment = 1,
            Value = Math.Clamp((decimal)current, 7, 48),
            Width = 80,
        };
    }

    private static void AddCheckRow(TableLayoutPanel table, CheckBox chk)
    {
        int row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        table.Controls.Add(chk, 0, row);
        table.SetColumnSpan(chk, 2);
        chk.Anchor = AnchorStyles.Left;
    }

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

    private static void AddRow(TableLayoutPanel table, string label, Control control)
    {
        int row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        table.Controls.Add(new Label
        {
            Text = label,
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill,
        }, 0, row);
        if (control is NumericUpDown)
            control.Anchor = AnchorStyles.Left; // 數字框固定寬度靠左；ComboBox 已 Dock.Fill
        table.Controls.Add(control, 1, row);
    }
}
