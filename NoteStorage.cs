using System.Text.Json;

namespace MemoTack;

/// <summary>
/// 便箋資料的本機 JSON 存取。
/// 存放路徑：%APPDATA%\MemoTack\notes.json
/// </summary>
public static class NoteStorage
{
    private static readonly string StorageDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MemoTack");

    private static readonly string StorageFile = Path.Combine(StorageDir, "notes.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// 讀取失敗且無法備份原檔時為 true：本次執行一律不寫檔，避免空白資料蓋掉原本的便箋。
    /// </summary>
    private static bool _saveBlocked;

    /// <summary>載入時發生的問題（給系統匣氣泡提示用）；正常載入為 null</summary>
    public static string? LoadWarning { get; private set; }

    /// <summary>
    /// 讀取設定與所有便箋。檔案不存在或損毀時回傳預設值（容錯，不讓程式啟動失敗）。
    /// 損毀時會先把原檔備份，之後的存檔才不會讓原本的便箋永久消失。
    /// </summary>
    public static AppState Load()
    {
        if (!File.Exists(StorageFile))
            return new AppState();

        try
        {
            string json = File.ReadAllText(StorageFile);
            AppState? state;
            try
            {
                state = JsonSerializer.Deserialize<AppState>(json);
            }
            catch (JsonException)
            {
                // 相容舊版格式（純便箋陣列，無設定）
                var notes = JsonSerializer.Deserialize<List<NoteData>>(json);
                state = new AppState { Notes = notes! };
            }
            return Sanitize(state ?? new AppState());
        }
        catch
        {
            // JSON 損毀或讀取失敗（例如被防毒、雲端同步鎖住）：備份原檔後以預設狀態啟動
            BackupBrokenFile();
            return new AppState();
        }
    }

    /// <summary>把讀不了的存檔改名保留；連改名都失敗就封鎖本次執行的寫檔</summary>
    private static void BackupBrokenFile()
    {
        try
        {
            string backup = $"{StorageFile}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Move(StorageFile, backup);
            LoadWarning = $"便箋存檔無法讀取，已備份為 {Path.GetFileName(backup)}，本次以空白狀態啟動。";
        }
        catch
        {
            _saveBlocked = true;
            LoadWarning = "便箋存檔無法讀取，為保護原檔，本次執行的變更不會存檔。請重新啟動 MemoTack 再試。";
        }
    }

    /// <summary>
    /// 修正 JSON 裡的不合理值（手動編輯或舊版殘留），避免建立視窗時拋出例外導致啟動失敗。
    /// </summary>
    private static AppState Sanitize(AppState state)
    {
        state.Settings ??= new AppSettings();
        state.Notes ??= new List<NoteData>();
        state.Notes.RemoveAll(n => n == null);

        var s = state.Settings;
        var defaults = new AppSettings();
        if (string.IsNullOrWhiteSpace(s.TitleFontFamily)) s.TitleFontFamily = defaults.TitleFontFamily;
        if (string.IsNullOrWhiteSpace(s.ContentFontFamily)) s.ContentFontFamily = defaults.ContentFontFamily;
        s.TitleFontSize = ValidFontSize(s.TitleFontSize, defaults.TitleFontSize);
        s.ContentFontSize = ValidFontSize(s.ContentFontSize, defaults.ContentFontSize);
        s.Hotkey ??= string.Empty;
        s.RestoreHotkey ??= string.Empty;

        foreach (var n in state.Notes)
        {
            n.Content ??= string.Empty;
            n.FontSize = ValidFontSize(n.FontSize, s.ContentFontSize);
        }
        return state;
    }

    /// <summary>字級限制在設定視窗允許的範圍（7–48pt），不合理的值改用預設</summary>
    private static float ValidFontSize(float size, float fallback) =>
        float.IsFinite(size) && size >= 7f && size <= 48f ? size : fallback;

    /// <summary>
    /// 儲存設定與所有便箋。先寫入暫存檔再取代，避免寫到一半當機造成檔案損毀。
    /// </summary>
    public static void Save(AppState state)
    {
        if (_saveBlocked)
            return;

        try
        {
            Directory.CreateDirectory(StorageDir);
            string tmp = StorageFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(state, JsonOptions));
            File.Move(tmp, StorageFile, overwrite: true);
        }
        catch
        {
            // 儲存失敗不應讓程式崩潰（例如磁碟滿）
        }
    }
}
