namespace MemoTack;

/// <summary>
/// 全域外觀設定（所有便箋共用），可由設定視窗修改。
/// </summary>
public class AppSettings
{
    /// <summary>標題列按鈕字型</summary>
    public string TitleFontFamily { get; set; } = "Segoe UI";

    /// <summary>標題列按鈕字型大小（pt），標題列高度會隨之縮放</summary>
    public float TitleFontSize { get; set; } = 10f;

    /// <summary>便箋內容字型</summary>
    public string ContentFontFamily { get; set; } = "Segoe UI";

    /// <summary>便箋內容預設字型大小（pt）；個別便箋仍可用 Ctrl+滾輪 微調</summary>
    public float ContentFontSize { get; set; } = 11f;

    /// <summary>便箋是否顯示在最上層（置頂）</summary>
    public bool AlwaysOnTop { get; set; } = false;

    /// <summary>
    /// 顯示/隱藏所有便箋的全域快捷鍵。
    /// 留空 = 停用。被系統占用的組合（如 Win+S）會註冊失敗並提示。
    /// 注意：F12 被 Windows 保留給除錯器，不能使用。
    /// </summary>
    public string Hotkey { get; set; } = "Alt+F10";

    /// <summary>還原所有已關閉便箋的全域快捷鍵。留空 = 停用。</summary>
    public string RestoreHotkey { get; set; } = "Alt+F11";
}

/// <summary>
/// 整份存檔內容：全域設定 + 所有便箋。
/// </summary>
public class AppState
{
    public AppSettings Settings { get; set; } = new();
    public List<NoteData> Notes { get; set; } = new();
}
