namespace MemoTack;

/// <summary>
/// 程式進入點。不建立主視窗，改用 TrayApplicationContext 常駐系統匣。
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // 單一實例保護：避免「開機自動啟動」+「手動開啟」同時跑兩份
        using var mutex = new Mutex(initiallyOwned: true, @"Local\MemoTack_SingleInstance", out bool createdNew);
        if (!createdNew)
            return; // 已有一份在執行，直接離開

        // 啟用高 DPI、預設字型等 WinForms 初始化
        ApplicationConfiguration.Initialize();

        // 若已設定自動啟動但執行檔路徑改變，自動更新登錄值
        StartupManager.RefreshPathIfEnabled();

        // 以 ApplicationContext 執行：沒有主視窗，程式生命週期由系統匣控制
        Application.Run(new TrayApplicationContext());
    }
}
