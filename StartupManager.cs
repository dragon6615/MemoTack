using Microsoft.Win32;

namespace MemoTack;

/// <summary>
/// 開機（使用者登入）自動啟動管理。
/// 寫入 HKCU\Software\Microsoft\Windows\CurrentVersion\Run，
/// 只影響目前使用者、不需系統管理員權限。
/// 以登錄實際狀態為準，不另存在設定 JSON 裡。
/// </summary>
public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "MemoTack";

    /// <summary>
    /// 工作管理員「啟動應用程式」的停用/啟用旗標由 Windows 另存在這裡（REG_BINARY，
    /// 第一個位元組是奇數 = 停用）。只有使用者在工作管理員切換過才會出現。
    /// </summary>
    private const string ApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    /// <summary>目前執行檔完整路徑（加引號，避免路徑含空白）</summary>
    private static string ExePath => $"\"{Application.ExecutablePath}\"";

    /// <summary>是否已設定自動啟動</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            if (key?.GetValue(AppName) == null)
                return false;

            // 在工作管理員停用過：Run 值還在，但實際不會啟動
            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath);
            return approved?.GetValue(AppName) is not byte[] { Length: > 0 } flags || (flags[0] & 1) == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 啟用/停用自動啟動。停用時移除登錄值、不留殘留；
    /// 兩種情況都清掉工作管理員的旗標——啟用時才不會被舊的「已停用」擋住。
    /// </summary>
    public static void SetEnabled(bool enabled)
    {
        try
        {
            if (enabled)
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
                key.SetValue(AppName, ExePath);
            }
            else
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                key?.DeleteValue(AppName, throwOnMissingValue: false);
            }

            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath, writable: true);
            approved?.DeleteValue(AppName, throwOnMissingValue: false);
        }
        catch
        {
            // 寫入失敗（權限/群組原則限制）不影響程式其他功能
        }
    }

    /// <summary>
    /// 已啟用但登錄記錄的執行檔已不存在時（例如資料夾搬家、改裝到別的位置），
    /// 自動把登錄值更新成目前路徑。每次啟動呼叫一次即可。
    /// 原路徑仍存在就不動：避免執行開發版（bin\Debug）時搶走正式版的自動啟動。
    /// </summary>
    public static void RefreshPathIfEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key?.GetValue(AppName) is string current && current != ExePath &&
                !File.Exists(current.Trim().Trim('"')))
                key.SetValue(AppName, ExePath);
        }
        catch
        {
            // 忽略
        }
    }
}
