namespace MemoTack;

/// <summary>
/// 單張便箋的可序列化資料（POCO），供 System.Text.Json 使用。
/// </summary>
public class NoteData
{
    /// <summary>便箋唯一識別碼</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>便箋名稱（顯示在標題列，雙擊修改）；空字串 = 未命名</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>文字內容</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>視窗位置與大小</summary>
    public int X { get; set; } = 100;
    public int Y { get; set; } = 100;
    public int Width { get; set; } = 640;
    public int Height { get; set; } = 480;

    /// <summary>背景顏色索引（對應 Theme.Palette：0黃 1綠 2粉 3藍 4紫 5灰 6炭黑）</summary>
    public int ColorIndex { get; set; } = 0;

    /// <summary>
    /// 自訂顏色的色相（0–359）；null = 使用 ColorIndex 的預設色。
    /// 只存色相，亮度與飽和度由 Theme.FromHue 決定，日後調整配色公式時舊資料會跟著更新。
    /// 舊版程式讀不到這個欄位會略過，退回 ColorIndex 的顏色。
    /// </summary>
    public int? CustomHue { get; set; }

    /// <summary>文字字型大小（pt），可用 Ctrl+滾輪 調整</summary>
    public float FontSize { get; set; } = 11f;

    /// <summary>是否開啟中。false = 已關閉（保留資料，可從系統匣選單再開啟）</summary>
    public bool IsOpen { get; set; } = true;

    /// <summary>提醒的原定時間（本機時間）；null = 未設定提醒。重複提醒以此為基準往後推算</summary>
    public DateTime? ReminderAt { get; set; }

    /// <summary>提醒重複方式</summary>
    public ReminderRepeat ReminderRepeat { get; set; } = ReminderRepeat.None;

    /// <summary>每週重複的星期（位元遮罩，bit n = DayOfWeek n，週日為 bit 0）；只用於每週</summary>
    public int ReminderWeekDays { get; set; }

    /// <summary>每月重複的日子（1–31，該月沒有這天時在月底）；只用於每月</summary>
    public int ReminderMonthDay { get; set; }

    /// <summary>按「延後」後的響鈴時間；與 ReminderAt 分開存，延後才不會改掉重複提醒的基準時間</summary>
    public DateTime? ReminderSnoozeUntil { get; set; }

    /// <summary>
    /// 選單、通知等處用來辨識便箋的文字：有名稱用名稱，否則用內容第一個有字的行
    /// （去掉 markdown 符號，「- [ ] **寫週報**」→「寫週報」）；超過長度加「…」。
    /// （方法不會被序列化進 JSON）
    /// </summary>
    public string DisplayName(int maxLength)
    {
        string text = !string.IsNullOrWhiteSpace(Title)
            ? Title.Trim()
            : Content.Split('\n', '\r').Select(MarkdownSyntax.PlainText).FirstOrDefault(s => s.Length > 0) ?? "";
        if (text.Length == 0) return "（空白便箋）";
        return text.Length <= maxLength ? text : text[..maxLength] + "…";
    }
}
