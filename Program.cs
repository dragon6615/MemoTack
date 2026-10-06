namespace MemoTack;

/// <summary>
/// 程式進入點。不建立主視窗，改用 TrayApplicationContext 常駐系統匣。
/// </summary>
internal static class Program
{
    /// <summary>第二個實例用來通知第一個實例「把便箋叫出來」的事件名稱</summary>
    private const string ShowSignalName = @"Local\MemoTack_ShowNotes";

    [STAThread]
    private static void Main()
    {
        // 單一實例保護：避免「開機自動啟動」+「手動開啟」同時跑兩份
        using var mutex = new Mutex(initiallyOwned: true, @"Local\MemoTack_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            // 已有一份在執行：通知它顯示便箋後離開，否則便箋隱藏時使用者會以為程式沒反應
            if (EventWaitHandle.TryOpenExisting(ShowSignalName, out var existing))
            {
                using (existing)
                    existing.Set();
            }
            return;
        }

        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);

        // 未處理的 UI 例外交給 TrayApplicationContext 搶救存檔；必須在建立任何控制項前設定
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        // 啟用高 DPI、預設字型等 WinForms 初始化
        ApplicationConfiguration.Initialize();

        // 若已設定自動啟動但執行檔路徑改變，自動更新登錄值
        StartupManager.RefreshPathIfEnabled();

        var context = new TrayApplicationContext();
        Application.ThreadException += (_, e) => context.HandleUnhandledException(e.Exception);

        // 收到第二個實例的通知時（在執行緒集區），切回 UI 執行緒顯示便箋
        var ui = SynchronizationContext.Current!; // 建立控制項後已是 WindowsFormsSynchronizationContext
        var wait = ThreadPool.RegisterWaitForSingleObject(showSignal, (_, _) =>
        {
            try { ui.Post(_ => context.ShowAllNotes(), null); }
            catch { /* 程式正在結束，訊息迴圈已不在 */ }
        }, null, Timeout.Infinite, executeOnlyOnce: false);

        // 以 ApplicationContext 執行：沒有主視窗，程式生命週期由系統匣控制
        Application.Run(context);
        wait.Unregister(null);
    }
}
