namespace MemoTack;

/// <summary>
/// 設定單張便箋的提醒，由上而下：重複 → 日期／星期／日子 → 時間。依重複方式顯示需要的欄位（跟手機鬧鐘一樣）：
/// 不重複 → 日期＋時間（含快捷時間）；每天 → 只有時間（日期反灰）；
/// 平日／每週 → 時間＋星期（平日預設週一至週五，其實就是每週的預設組合）；每月 → 時間＋每月幾號。
/// 按「確定」後由呼叫端讀取 ReminderAt / Rule；按「清除提醒」則 Cleared = true。
/// </summary>
public class ReminderForm : StyledDialog
{
    /// <summary>重複選項的順序；「平日」是每週的預設組合，存檔時一律存成每週 + 星期遮罩</summary>
    private enum Mode { Once, Daily, Weekdays, Weekly, Monthly }

    private static readonly DayOfWeek[] WeekOrder =
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
    };

    private readonly Color _accent; // 便箋的標題列色，用於頂端色帶與選中的選項
    private readonly DateTimePicker _time;
    private readonly DateTimePicker _date;
    private readonly RadioButton[] _modeButtons;
    private readonly CheckBox[] _dayChips;   // 依 WeekOrder 排列：一 … 日
    private readonly NumericUpDown _monthDay;
    private readonly Control[] _dateRow, _weekRow, _monthRow, _quickRow; // 各列的標籤＋內容，切換時整列顯示／隱藏
    private readonly Label _summary;
    private readonly Button _btnOk;
    private bool _syncing; // 程式自己改選項時，不要觸發連動

    /// <summary>確定後的提醒時間：第一次響鈴的時間（重複提醒已推到第一個未來的時間）</summary>
    public DateTime ReminderAt { get; private set; }

    /// <summary>確定後的重複規則</summary>
    public RepeatRule Rule { get; private set; }

    /// <summary>使用者按了「清除提醒」</summary>
    public bool Cleared { get; private set; }

    public ReminderForm(NoteData data, Color accent) : base("設定提醒")
    {
        _accent = accent;
        StartPosition = FormStartPosition.CenterParent;

        // 已有提醒就帶入原本的設定，否則預設下一個整點、不重複
        var now = DateTime.Now;
        var initial = data.ReminderAt ?? now.Date.AddHours(now.Hour + 1);
        var rule = RepeatRule.From(data);

        // ---- 時間 ----
        _time = new DateTimePicker
        {
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "HH:mm",
            ShowUpDown = true,
            Value = initial,
            Width = TextRenderer.MeasureText("00:00", Font).Width + Dpi(44),
            Margin = new Padding(0, Dpi(4), 0, Dpi(4)),
        };

        // ---- 重複：五個並排的切換按鈕 ----
        var repeatRow = MakeFlow();
        _modeButtons = new[] { "不重複", "每天", "平日", "每週", "每月" }.Select(MakeToggle).ToArray();
        foreach (var rb in _modeButtons)
            repeatRow.Controls.Add(rb);

        // ---- 日期（不重複；每天時反灰）----
        _date = new DateTimePicker
        {
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "yyyy/MM/dd  dddd",
            Value = initial.Date,
            Width = TextRenderer.MeasureText("2026/10/07  星期三", Font).Width + Dpi(44),
            Margin = new Padding(0, Dpi(4), 0, Dpi(4)),
        };

        // ---- 星期（平日／每週）----
        var weekRow = MakeFlow();
        _dayChips = WeekOrder.Select(d => MakeDayChip(ReminderSchedule.DayName(d))).ToArray();
        foreach (var chip in _dayChips)
            weekRow.Controls.Add(chip);

        // ---- 每月幾號 ----
        var monthRow = MakeFlow();
        _monthDay = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 31,
            Value = rule.MonthDay is >= 1 and <= 31 ? rule.MonthDay : initial.Day,
            Width = TextRenderer.MeasureText("31", Font).Width + Dpi(36),
            Margin = new Padding(0, 0, Dpi(6), 0),
        };
        monthRow.Controls.Add(new Label { Text = "每月", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, Dpi(6), 0) });
        monthRow.Controls.Add(_monthDay);
        monthRow.Controls.Add(new Label { Text = "日", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, Dpi(12), 0) });
        monthRow.Controls.Add(new Label
        {
            Text = "（該月沒有這天時，在月底提醒）",
            AutoSize = true,
            ForeColor = TextMuted,
            Font = OwnFont(9f),
            Anchor = AnchorStyles.Left,
        });

        // ---- 快捷時間（不重複）：只改選擇器的值，按「確定」才生效 ----
        var quickRow = MakeFlow();
        quickRow.Controls.Add(MakeChip("10 分鐘後", () => DateTime.Now.AddMinutes(10)));
        quickRow.Controls.Add(MakeChip("1 小時後", () => DateTime.Now.AddHours(1)));
        if (now.Hour < 20)
            quickRow.Controls.Add(MakeChip("今晚 8 點", () => DateTime.Today.AddHours(20)));
        quickRow.Controls.Add(MakeChip("明天 9:00", () => DateTime.Today.AddDays(1).AddHours(9)));

        // ---- 下次提醒摘要（即時更新）----
        _summary = new Label { AutoSize = true, Margin = new Padding(0, Dpi(10), 0, 0), Font = OwnFont(10f, FontStyle.Bold) };

        // 順序依思考流程：先決定多久一次（重複）→ 哪一天（日期／星期／日子）→ 幾點（時間）
        var body = MakeBody();
        AddRow(body, "重複", repeatRow);
        _dateRow = new Control[] { AddRow(body, "日期", _date), _date };
        _weekRow = new Control[] { AddRow(body, "星期", weekRow), weekRow };
        _monthRow = new Control[] { AddRow(body, "日子", monthRow), monthRow };
        AddRow(body, "時間", _time);
        _quickRow = new Control[] { AddRow(body, "快捷", quickRow), quickRow };
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
        SetLayout(MakeHeader(Theme.IconReminder, "設定提醒", data.DisplayName(24), _accent), body, MakeFooter(clear, _btnOk, btnCancel));

        AcceptButton = _btnOk;
        CancelButton = btnCancel;

        // ---- 帶入原本的設定 ----
        var mode = rule.Kind switch
        {
            ReminderRepeat.Daily => Mode.Daily,
            ReminderRepeat.Weekdays => Mode.Weekdays,
            ReminderRepeat.Weekly when (rule.WeekDays & RepeatRule.AllDaysMask) == RepeatRule.WeekdayMask => Mode.Weekdays,
            ReminderRepeat.Weekly => Mode.Weekly,
            ReminderRepeat.Monthly => Mode.Monthly,
            _ => Mode.Once,
        };
        SetDayMask(rule.Kind == ReminderRepeat.Weekly ? rule.WeekDays : RepeatRule.Bit(initial.DayOfWeek));
        _time.ValueChanged += (_, _) => UpdateSummary();
        _date.ValueChanged += (_, _) => UpdateSummary();
        _monthDay.ValueChanged += (_, _) => UpdateSummary();

        // 以最寬的版面（不重複：多一列快捷時間）當最小寬度，切換重複方式時視窗才不會忽寬忽窄
        SetMode(Mode.Once);
        LockCurrentWidth();
        SetMode(mode);
    }

    // ---------- 重複方式切換 ----------

    private Mode CurrentMode => (Mode)Math.Max(0, Array.FindIndex(_modeButtons, b => b.Checked));

    /// <summary>切換重複方式：勾選對應按鈕、帶入預設星期、顯示需要的欄位</summary>
    private void SetMode(Mode mode)
    {
        _syncing = true;
        _modeButtons[(int)mode].Checked = true;
        if (mode == Mode.Weekdays)
            SetDayMask(RepeatRule.WeekdayMask); // 平日 = 預設勾好週一至週五
        else if (mode == Mode.Weekly && DayMask() == RepeatRule.WeekdayMask)
            SetDayMask(RepeatRule.Bit(_date.Value.DayOfWeek)); // 從平日切到每週：改成選日期當天的星期
        _syncing = false;

        // 每天只需要時間：日期列仍顯示但反灰（跟手機鬧鐘一樣，讓人知道日期不重要）
        _date.Enabled = mode == Mode.Once;
        SetRowVisible(_dateRow, mode is Mode.Once or Mode.Daily);
        SetRowVisible(_weekRow, mode is Mode.Weekdays or Mode.Weekly);
        SetRowVisible(_monthRow, mode == Mode.Monthly);
        SetRowVisible(_quickRow, mode == Mode.Once);
        UpdateSummary();
    }

    private static void SetRowVisible(Control[] row, bool visible)
    {
        foreach (var c in row)
            c.Visible = visible;
    }

    /// <summary>使用者自己點星期：剛好是週一至週五就標成「平日」，否則是「每週」</summary>
    private void OnDayChipChanged()
    {
        if (_syncing)
            return;
        _syncing = true;
        _modeButtons[(int)(DayMask() == RepeatRule.WeekdayMask ? Mode.Weekdays : Mode.Weekly)].Checked = true;
        _syncing = false;
        UpdateSummary();
    }

    private int DayMask()
    {
        int mask = 0;
        for (int i = 0; i < WeekOrder.Length; i++)
            if (_dayChips[i].Checked) mask |= RepeatRule.Bit(WeekOrder[i]);
        return mask;
    }

    private void SetDayMask(int mask)
    {
        bool syncing = _syncing;
        _syncing = true;
        for (int i = 0; i < WeekOrder.Length; i++)
            _dayChips[i].Checked = (mask & RepeatRule.Bit(WeekOrder[i])) != 0;
        _syncing = syncing;
    }

    private RepeatRule CurrentRule() => CurrentMode switch
    {
        Mode.Daily => new RepeatRule(ReminderRepeat.Daily),
        Mode.Weekdays or Mode.Weekly => new RepeatRule(ReminderRepeat.Weekly, WeekDays: DayMask()),
        Mode.Monthly => new RepeatRule(ReminderRepeat.Monthly, MonthDay: (int)_monthDay.Value),
        _ => RepeatRule.None,
    };

    // ---------- 時間計算 ----------

    /// <summary>
    /// 依目前選擇算出第一次提醒時間：不重複 → 選的日期＋時間（不能在過去）；
    /// 重複 → 從今天的這個時刻起，第一個符合規則且在未來的時間。不合法時回傳錯誤訊息。
    /// </summary>
    private (DateTime? At, string? Error) Resolve()
    {
        var t = _time.Value;
        var timeOfDay = new TimeSpan(t.Hour, t.Minute, 0);
        var now = DateTime.Now;
        var rule = CurrentRule();

        if (rule.Kind == ReminderRepeat.None)
        {
            var at = DateTime.SpecifyKind(_date.Value.Date + timeOfDay, DateTimeKind.Local);
            return at > now ? (at, null) : (null, "這個時間已經過了，請選擇未來的時間");
        }
        if (rule.Kind == ReminderRepeat.Weekly && rule.WeekDays == 0)
            return (null, "請至少選一天");

        var today = DateTime.SpecifyKind(DateTime.Today + timeOfDay, DateTimeKind.Local);
        return (ReminderSchedule.FirstOnOrAfter(today, rule, now), null);
    }

    private void UpdateSummary()
    {
        if (_summary is null || _btnOk is null)
            return; // 建構途中設定預設選項也會觸發，這時控制項還沒建好

        var (at, error) = Resolve();
        _btnOk.Enabled = at != null;
        if (at == null)
        {
            _summary.Text = error;
            _summary.ForeColor = Danger;
            return;
        }
        var rule = CurrentRule();
        string repeat = rule.Kind == ReminderRepeat.None ? "" : $"（{ReminderSchedule.Describe(rule)}）";
        _summary.Text = $"下次提醒：{FormatLong(at.Value)}{repeat}";
        _summary.ForeColor = TextStrong;
    }

    /// <summary>「今天 14:30」「明天（週三）09:00」「10/12（週一）09:00」</summary>
    private static string FormatLong(DateTime t)
    {
        var today = DateTime.Today;
        string week = "（週" + ReminderSchedule.DayName(t.DayOfWeek) + "）";
        string day = t.Date == today ? "今天"
            : t.Date == today.AddDays(1) ? "明天" + week
            : t.ToString("M/d") + week;
        return $"{day} {t:HH:mm}";
    }

    private void Confirm()
    {
        if (Resolve().At is not { } at)
            return;
        ReminderAt = at;
        Rule = CurrentRule();
        DialogResult = DialogResult.OK;
    }

    // ---------- 控制項工廠 ----------

    /// <summary>快捷時間的扁平小按鈕：同時設定日期與時間</summary>
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

    /// <summary>重複方式：外觀像按鈕的單選鈕，選中時用便箋顏色</summary>
    private RadioButton MakeToggle(string text, int index)
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
        rb.CheckedChanged += (_, _) =>
        {
            if (rb.Checked && !_syncing)
                SetMode((Mode)index);
        };
        return rb;
    }

    /// <summary>星期按鈕：可複選，選中時用便箋顏色</summary>
    private CheckBox MakeDayChip(string text)
    {
        int size = TextRenderer.MeasureText("日", Font).Height + Dpi(12);
        var chip = new CheckBox
        {
            Text = text,
            Appearance = Appearance.Button,
            FlatStyle = FlatStyle.Flat,
            TextAlign = ContentAlignment.MiddleCenter,
            Size = new Size(size, size),
            BackColor = ChipBack,
            Margin = new Padding(0, 0, Dpi(4), 0),
            Cursor = Cursors.Hand,
        };
        chip.FlatAppearance.BorderColor = ChipBorder;
        chip.FlatAppearance.CheckedBackColor = _accent;
        chip.FlatAppearance.MouseOverBackColor = ChipHover;
        chip.CheckedChanged += (_, _) => OnDayChipChanged();
        return chip;
    }
}
