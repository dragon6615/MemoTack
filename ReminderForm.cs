namespace MemoTack;

/// <summary>
/// 設定單張便箋的提醒：日期、時間、快捷時間、重複方式、清除提醒。
/// 按「確定」後由呼叫端讀取 ReminderAt / Repeat；按「清除提醒」則 Cleared = true。
/// </summary>
public class ReminderForm : StyledDialog
{
    private static readonly ReminderRepeat[] RepeatOptions =
    {
        ReminderRepeat.None, ReminderRepeat.Daily, ReminderRepeat.Weekdays, ReminderRepeat.Weekly,
    };

    private readonly Color _accent; // 便箋的標題列色，用於頂端色帶與選中的重複選項
    private readonly DateTimePicker _date;
    private readonly DateTimePicker _time;
    private readonly RadioButton[] _repeatButtons;
    private readonly Label _summary;
    private readonly Button _btnOk;

    /// <summary>確定後的提醒時間（已校正：秒數歸零、平日跳過週末、重複提醒已推到未來）</summary>
    public DateTime ReminderAt { get; private set; }

    public ReminderRepeat Repeat { get; private set; }

    /// <summary>使用者按了「清除提醒」</summary>
    public bool Cleared { get; private set; }

    public ReminderForm(NoteData data, Color accent) : base("設定提醒")
    {
        _accent = accent;
        StartPosition = FormStartPosition.CenterParent;

        // 已有提醒就帶入原定時間，否則預設下一個整點
        var now = DateTime.Now;
        var initial = data.ReminderAt ?? now.Date.AddHours(now.Hour + 1);

        // ---- 日期 + 時間 ----
        _date = new DateTimePicker
        {
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "yyyy/MM/dd  dddd",
            Value = initial.Date,
            Width = TextRenderer.MeasureText("2026/10/07  星期三", Font).Width + Dpi(44),
            Margin = new Padding(0, 0, Dpi(8), 0),
        };
        _time = new DateTimePicker
        {
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "HH:mm",
            ShowUpDown = true, // 時間用上下調整，不需要月曆
            Value = initial,
            Width = TextRenderer.MeasureText("00:00", Font).Width + Dpi(44),
            Margin = Padding.Empty,
        };
        _date.ValueChanged += (_, _) => UpdateSummary();
        _time.ValueChanged += (_, _) => UpdateSummary();
        var dateTimeRow = MakeFlow();
        dateTimeRow.Controls.Add(_date);
        dateTimeRow.Controls.Add(_time);

        // ---- 快捷時間：只改選擇器的值，按「確定」才生效 ----
        var quickRow = MakeFlow();
        quickRow.Controls.Add(MakeChip("10 分鐘後", () => DateTime.Now.AddMinutes(10)));
        quickRow.Controls.Add(MakeChip("1 小時後", () => DateTime.Now.AddHours(1)));
        if (now.Hour < 20)
            quickRow.Controls.Add(MakeChip("今晚 8 點", () => DateTime.Today.AddHours(20)));
        quickRow.Controls.Add(MakeChip("明天 9:00", () => DateTime.Today.AddDays(1).AddHours(9)));

        // ---- 重複：四個並排的切換按鈕 ----
        var repeatRow = MakeFlow();
        _repeatButtons = RepeatOptions.Select(r => MakeToggle(r == ReminderRepeat.Weekdays ? "平日" : ReminderSchedule.RepeatName(r))).ToArray();
        foreach (var rb in _repeatButtons)
            repeatRow.Controls.Add(rb);
        _repeatButtons[Math.Max(0, Array.IndexOf(RepeatOptions, data.ReminderRepeat))].Checked = true;

        // ---- 下次提醒摘要（即時更新，平日遇週末會直接顯示順延後的日期） ----
        _summary = new Label { AutoSize = true, Margin = new Padding(0, Dpi(10), 0, 0), Font = OwnFont(10f, FontStyle.Bold) };

        var body = MakeBody();
        AddRow(body, "時間", dateTimeRow);
        AddRow(body, "快捷", quickRow);
        AddRow(body, "重複", repeatRow);
        body.Controls.Add(_summary, 1, body.RowCount++);

        // ---- 底部：左側清除提醒，右側確定 / 取消 ----
        _btnOk = MakeButton("確定", primary: true);
        _btnOk.Click += (_, _) => Confirm();
        var btnCancel = MakeButton("取消", primary: false);
        btnCancel.DialogResult = DialogResult.Cancel;

        LinkLabel? clear = null;
        if (data.ReminderAt != null)
        {
            clear = new LinkLabel
            {
                Text = "清除提醒",
                AutoSize = true,
                LinkColor = Danger,
                ActiveLinkColor = Danger,
                LinkBehavior = LinkBehavior.HoverUnderline,
                Margin = new Padding(Dpi(4), 0, Dpi(16), 0),
            };
            clear.LinkClicked += (_, _) =>
            {
                Cleared = true;
                DialogResult = DialogResult.OK;
            };
        }

        // 色帶用便箋的顏色並顯示便箋名稱，一看就知道在設定哪一張
        SetLayout(MakeHeader("⏰  設定提醒", data.DisplayName(24), _accent), body, MakeFooter(clear, _btnOk, btnCancel));

        AcceptButton = _btnOk;
        CancelButton = btnCancel;
        UpdateSummary();
    }

    // ---------- 時間計算 ----------

    private ReminderRepeat SelectedRepeat =>
        RepeatOptions[Math.Max(0, Array.FindIndex(_repeatButtons, b => b.Checked))];

    /// <summary>
    /// 依目前選擇算出實際提醒時間：秒數歸零、平日跳過週末、重複提醒若已過則推到下一次。
    /// 不重複的提醒設在過去時回傳 null。
    /// </summary>
    private DateTime? ResolveTime()
    {
        var d = _date.Value;
        var t = _time.Value;
        var at = new DateTime(d.Year, d.Month, d.Day, t.Hour, t.Minute, 0, DateTimeKind.Local);
        var repeat = SelectedRepeat;
        if (repeat == ReminderRepeat.Weekdays)
            at = ReminderSchedule.SkipWeekend(at);

        var now = DateTime.Now;
        if (at > now) return at;
        if (repeat == ReminderRepeat.None) return null;
        return ReminderSchedule.Next(at, repeat, now); // 重複提醒：從下一次開始
    }

    private void UpdateSummary()
    {
        if (_summary is null || _btnOk is null)
            return; // 建構途中設定預設選項也會觸發，這時控制項還沒建好

        var at = ResolveTime();
        _btnOk.Enabled = at != null;
        if (at == null)
        {
            _summary.Text = "這個時間已經過了，請選擇未來的時間";
            _summary.ForeColor = Danger;
            return;
        }
        _summary.Text = "下次提醒：" + FormatLong(at.Value);
        _summary.ForeColor = TextStrong;
    }

    /// <summary>「今天 14:30」「明天（週三）09:00」「10/12（週一）09:00」</summary>
    private static string FormatLong(DateTime t)
    {
        var today = DateTime.Today;
        string week = "（週" + "日一二三四五六"[(int)t.DayOfWeek] + "）";
        string day = t.Date == today ? "今天"
            : t.Date == today.AddDays(1) ? "明天" + week
            : t.ToString("M/d") + week;
        return $"{day} {t:HH:mm}";
    }

    private void Confirm()
    {
        if (ResolveTime() is not { } at)
            return;
        ReminderAt = at;
        Repeat = SelectedRepeat;
        DialogResult = DialogResult.OK;
    }

    // ---------- 控制項工廠 ----------

    /// <summary>快捷時間的扁平小按鈕</summary>
    private Button MakeChip(string text, Func<DateTime> value)
    {
        var btn = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlatStyle = FlatStyle.Flat,
            BackColor = ChipBack,
            Padding = new Padding(Dpi(6), Dpi(2), Dpi(6), Dpi(2)),
            Margin = new Padding(0, 0, Dpi(6), 0),
            Cursor = Cursors.Hand,
        };
        btn.FlatAppearance.BorderColor = ChipBorder;
        btn.FlatAppearance.MouseOverBackColor = ChipHover;
        btn.Click += (_, _) =>
        {
            var v = value();
            _date.Value = v.Date;
            _time.Value = v;
        };
        return btn;
    }

    /// <summary>重複選項：外觀像按鈕的單選鈕，選中時用便箋顏色</summary>
    private RadioButton MakeToggle(string text)
    {
        var rb = new RadioButton
        {
            Text = text,
            Appearance = Appearance.Button,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = ChipBack,
            Padding = new Padding(Dpi(8), Dpi(2), Dpi(8), Dpi(2)),
            Margin = new Padding(0, 0, Dpi(6), 0),
            Cursor = Cursors.Hand,
        };
        rb.FlatAppearance.BorderColor = ChipBorder;
        rb.FlatAppearance.CheckedBackColor = _accent;
        rb.FlatAppearance.MouseOverBackColor = ChipHover;
        rb.CheckedChanged += (_, _) => UpdateSummary();
        return rb;
    }
}
