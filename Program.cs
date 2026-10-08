namespace MemoTack;

/// <summary>
/// 程式進入點。不建立主視窗，改用 TrayApplicationContext 常駐系統匣。
/// </summary>
internal static class Program
{
    /// <summary>第二個實例用來通知第一個實例「把便箋叫出來」的事件名稱</summary>
    private const string ShowSignalName = @"Local\MemoTack_ShowNotes" + DevSuffix;

#if DEBUG
    /// <summary>
    /// 開發版（Debug 建置）用獨立的單一實例鎖與資料夾，可與已安裝的正式版同時執行，
    /// 不會碰到真實便箋，也不會改寫開機自動啟動的路徑。
    /// </summary>
    public const string DevSuffix = "-Dev";
#else
    public const string DevSuffix = "";
#endif

    private static Mutex? s_singleInstance;

    /// <summary>
    /// 提早放開單一實例鎖（自動更新時，在啟動安裝程式之前呼叫）：
    /// 安裝程式以這個 mutex（installer.iss 的 AppMutex）判斷 MemoTack 是否還在執行，
    /// 靜默安裝時若程式還沒完全結束會直接中止，所以不能等到程序結束才放開。必須在 UI 執行緒呼叫。
    /// </summary>
    public static void ReleaseSingleInstance()
    {
        try { s_singleInstance?.ReleaseMutex(); }
        catch (ApplicationException) { /* 已經放開過 */ }
        s_singleInstance = null;
    }

    [STAThread]
    private static void Main()
    {
        // 單一實例保護：避免「開機自動啟動」+「手動開啟」同時跑兩份
        using var mutex = new Mutex(initiallyOwned: true, @"Local\MemoTack_SingleInstance" + DevSuffix, out bool createdNew);
        s_singleInstance = createdNew ? mutex : null;
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
#if !DEBUG
        StartupManager.RefreshPathIfEnabled();
#endif

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
