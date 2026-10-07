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
    /// 預設選 Win+Alt：一般程式不用（Ctrl/Alt+字母會搶走程式自己的快捷鍵），
    /// 不必按 Fn，單手按得到；N/S 被 OneNote 與 Microsoft 便利貼占用，所以用 Q/Z。
    /// 改預設值只影響新安裝；舊使用者 JSON 裡存的值照舊（分不出是沿用預設還是自己選的）。
    /// </summary>
    public string Hotkey { get; set; } = "Win+Alt+Q";

    /// <summary>還原所有已關閉便箋的全域快捷鍵。留空 = 停用。</summary>
    public string RestoreHotkey { get; set; } = "Win+Alt+Z";

    /// <summary>自動檢查更新（啟動後與每天一次，連線到 GitHub）；舊版存檔沒有這個欄位時預設開啟</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>使用者選了「略過這個版本」的版本號（x.y.z）；自動檢查時不再詢問這一版</summary>
    public string SkippedVersion { get; set; } = string.Empty;
}

/// <summary>
/// 整份存檔內容：全域設定 + 所有便箋。
/// </summary>
public class AppState
{
    public AppSettings Settings { get; set; } = new();
    public List<NoteData> Notes { get; set; } = new();
}
