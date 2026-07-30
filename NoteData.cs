namespace MemoTack;

/// <summary>
/// 單張便箋的可序列化資料（POCO），供 System.Text.Json 使用。
/// </summary>
public class NoteData
{
    /// <summary>便箋唯一識別碼</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>文字內容</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>視窗位置與大小</summary>
    public int X { get; set; } = 100;
    public int Y { get; set; } = 100;
    public int Width { get; set; } = 640;
    public int Height { get; set; } = 480;

    /// <summary>背景顏色索引（對應 NoteForm.Palette：0黃 1綠 2粉 3藍）</summary>
    public int ColorIndex { get; set; } = 0;

    /// <summary>文字字型大小（pt），可用 Ctrl+滾輪 調整</summary>
    public float FontSize { get; set; } = 11f;

    /// <summary>是否開啟中。false = 已關閉（保留資料，可從系統匣選單再開啟）</summary>
    public bool IsOpen { get; set; } = true;
}
