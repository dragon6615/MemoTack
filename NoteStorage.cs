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
    /// 讀取設定與所有便箋。檔案不存在或損毀時回傳預設值（容錯，不讓程式啟動失敗）。
    /// </summary>
    public static AppState Load()
    {
        try
        {
            if (!File.Exists(StorageFile))
                return new AppState();

            string json = File.ReadAllText(StorageFile);
            try
            {
                return JsonSerializer.Deserialize<AppState>(json) ?? new AppState();
            }
            catch (JsonException)
            {
                // 相容舊版格式（純便箋陣列，無設定）
                var notes = JsonSerializer.Deserialize<List<NoteData>>(json) ?? new List<NoteData>();
                return new AppState { Notes = notes };
            }
        }
        catch
        {
            // JSON 損毀或讀取失敗：以預設狀態啟動
            return new AppState();
        }
    }

    /// <summary>
    /// 儲存設定與所有便箋。先寫入暫存檔再取代，避免寫到一半當機造成檔案損毀。
    /// </summary>
    public static void Save(AppState state)
    {
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
