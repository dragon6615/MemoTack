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

    /// <summary>目前執行檔完整路徑（加引號，避免路徑含空白）</summary>
    private static string ExePath => $"\"{Application.ExecutablePath}\"";

    /// <summary>是否已設定自動啟動</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(AppName) != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>啟用/停用自動啟動</summary>
    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (enabled)
                key.SetValue(AppName, ExePath);
            else
                key.DeleteValue(AppName, throwOnMissingValue: false);
        }
        catch
        {
            // 寫入失敗（權限/群組原則限制）不影響程式其他功能
        }
    }

    /// <summary>
    /// 已啟用但執行檔路徑改變時（例如專案資料夾搬家、改用發佈版），
    /// 自動把登錄值更新成目前路徑。每次啟動呼叫一次即可。
    /// </summary>
    public static void RefreshPathIfEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key?.GetValue(AppName) is string current && current != ExePath)
                key.SetValue(AppName, ExePath);
        }
        catch
        {
            // 忽略
        }
    }
}
