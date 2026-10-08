using Microsoft.Win32;

namespace MemoTack;

/// <summary>
/// 應用程式核心：常駐系統匣（NotifyIcon），管理所有便箋的建立、關閉、
/// 顯示/隱藏，以及啟動還原與結束存檔。
/// </summary>
public class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly List<NoteForm> _notes = new();
    private readonly List<NoteData> _closedNotes = new(); // 已關閉但保留的便箋
    private readonly ToolStripMenuItem _closedMenu;
    private readonly ToolStripMenuItem _toggleMenu;
    private readonly ToolStripMenuItem _reminderMenu;
    private readonly System.Windows.Forms.Timer _reminderTimer;
    private readonly System.Windows.Forms.Timer _updateTimer;
    private bool _checkingUpdate;
    private readonly Icon _iconNormal = CreateTrayIcon(hidden: false); // 黃色：便箋顯示中
    private readonly Icon _iconHidden = CreateTrayIcon(hidden: true);  // 灰色：便箋隱藏中
    private readonly AppSettings _settings;
    private readonly System.Windows.Forms.Timer _saveTimer;
    private readonly HotkeyManager _hotkey;
    private const int HotkeyToggleId = 1;  // 顯示/隱藏所有便箋
    private const int HotkeyRestoreId = 2; // 還原所有已關閉便箋
    private bool _notesVisible = true;
    private bool _exiting;

    public TrayApplicationContext()
    {
        // ---- 讀取設定與便箋 ----
        var state = NoteStorage.Load();
        _settings = state.Settings;

        // 舊版預設快捷鍵遷移（Alt+F10 不遷移：可能是使用者自己選的）：
        // - Ctrl+Alt+S 是更早的預設值
        // - Alt+F12 不會生效（F12 被 Windows 保留給除錯器）
        if (_settings.Hotkey is "Ctrl+Alt+S" or "Alt+F12")
            _settings.Hotkey = new AppSettings().Hotkey;

        // 防抖存檔計時器：變更後 1.5 秒才寫檔，避免拖曳時瘋狂寫入
        _saveTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SaveAll();
        };

        // ---- 系統匣圖示與右鍵選單 ----
        var menu = new ContextMenuStrip();
        menu.Items.Add("新增便箋", null, (_, _) => CreateNote(null));

        _toggleMenu = new ToolStripMenuItem("隱藏所有便箋"); // 文字於開啟選單時依狀態更新
        _toggleMenu.Click += (_, _) => ToggleAllNotes();
        menu.Items.Add(_toggleMenu);

        _closedMenu = new ToolStripMenuItem("已關閉的便箋"); // 子選單內容於開啟選單時重建
        menu.Items.Add(_closedMenu);

        _reminderMenu = new ToolStripMenuItem("即將到來的提醒"); // 子選單內容於開啟選單時重建
        menu.Items.Add(_reminderMenu);

        menu.Items.Add("設定...", null, (_, _) => OpenSettings());
        menu.Items.Add("檢查更新...", null, async (_, _) => await CheckForUpdatesAsync(manual: true));
        menu.Items.Add("關於 MemoTack...", null, (_, _) => { using var dlg = new AboutForm(); dlg.ShowDialog(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("結束", null, (_, _) => ExitApp());
        MenuStyle.Apply(menu);
        menu.Opening += (_, _) =>
        {
            RebuildClosedMenu();
            RebuildReminderMenu();
            _toggleMenu.Text = _notesVisible ? "隱藏所有便箋" : "顯示所有便箋";
            _toggleMenu.Enabled = _notes.Count > 0;
        };

        _trayIcon = new NotifyIcon
        {
            Icon = _iconNormal,
            Text = "MemoTack 桌面便箋",
            ContextMenuStrip = menu,
            Visible = true,
        };
        // 單擊 = 把便箋叫到最前面（非置頂模式下便箋常被其他視窗蓋住，又不在工具列與 Alt+Tab 裡）。
        // 刻意不用雙擊：雙擊的第一下會先觸發單擊，兩個動作混在一起反而難以預期
        _trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ShowAllNotes();
        };
        _trayIcon.BalloonTipClicked += (_, _) => FocusRingingNote();

        // Windows 關機/登出時搶先存檔
        SystemEvents.SessionEnding += OnSessionEnding;

        // ---- 提醒：每 15 秒檢查一次；從睡眠喚醒時立刻補檢查 ----
        _reminderTimer = new System.Windows.Forms.Timer { Interval = 15_000 };
        _reminderTimer.Tick += (_, _) => CheckReminders();

        // ---- 自動檢查更新：啟動後 30 秒（不拖慢開機），之後每天一次（很多人好幾天不關程式）----
        _updateTimer = new System.Windows.Forms.Timer { Interval = 30_000 };
        _updateTimer.Tick += async (_, _) =>
        {
            if (_updateTimer.Interval == 30_000)
                UpdateChecker.CleanupDownloads(); // 第一次觸發時，自動更新的安裝程式早已結束，可以刪掉安裝檔
            _updateTimer.Interval = 24 * 60 * 60 * 1000;
            await CheckForUpdatesAsync(manual: false);
        };
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        // ---- 全域快捷鍵 ----
        _hotkey = new HotkeyManager();
        _hotkey.Pressed += id =>
        {
            if (id == HotkeyToggleId) ToggleFromHotkey();
            else if (id == HotkeyRestoreId) RestoreAllClosed();
        };
        ApplyHotkey();

        // ---- 啟動還原：開啟中的直接顯示，已關閉的進入保留清單 ----
        foreach (var data in state.Notes)
        {
            if (data.IsOpen) CreateNote(data);
            else _closedNotes.Add(data);
        }
        if (_notes.Count == 0 && _closedNotes.Count == 0)
            CreateNote(null); // 第一次啟動：給一張預設便箋

        UpdateTrayState();

        // 存檔讀取失敗（已備份或暫停寫檔）：告知使用者，不要默默以空白狀態啟動
        if (NoteStorage.LoadWarning is { } warning)
            _trayIcon.ShowBalloonTip(8000, "MemoTack", warning, ToolTipIcon.Warning);

        // 啟動時先檢查一次：程式沒在執行期間錯過的提醒立刻補發
        CheckReminders();
        _reminderTimer.Start();
        _updateTimer.Start();
    }

    /// <summary>
    /// 更新系統匣圖示與提示文字，讓使用者一眼看出目前狀態：
    /// 便箋隱藏中 → 圖示變灰；懸停顯示張數與狀態。
    /// </summary>
    private void UpdateTrayState()
    {
        bool hidden = !_notesVisible && _notes.Count > 0;
        _trayIcon.Icon = hidden ? _iconHidden : _iconNormal;

        string text = $"MemoTack — {_notes.Count} 張便箋";
        if (_closedNotes.Count > 0) text += $"，{_closedNotes.Count} 張已關閉";
        if (hidden) text += "（隱藏中，點一下顯示）";
        _trayIcon.Text = text.Length <= 63 ? text : text[..63]; // NotifyIcon.Text 長度上限保護
    }

    // ---------- 便箋管理 ----------

    /// <summary>
    /// 建立一張便箋並顯示。data 為 null 時建立新便箋（可指定位置）。
    /// activate=false 時顯示但不搶鍵盤焦點（提醒自動開啟已關閉的便箋時用）。
    /// </summary>
    private NoteForm CreateNote(NoteData? data, Point? location = null, bool activate = true)
    {
        if (data == null)
        {
            data = new NoteData { FontSize = _settings.ContentFontSize };
            if (location.HasValue)
            {
                data.X = location.Value.X;
                data.Y = location.Value.Y;
            }
            else
            {
                // 預設放在主螢幕工作區中央，並依現有張數微幅階梯偏移，避免整疊蓋在一起
                var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1024, 768);
                int offset = (_notes.Count % 8) * 24;
                data.X = wa.Left + (wa.Width - data.Width) / 2 + offset;
                data.Y = wa.Top + (wa.Height - data.Height) / 2 + offset;
            }
        }

        data.IsOpen = true;
        var form = new NoteForm(data, _settings);
        form.NewNoteRequested += f => CreateNote(null, new Point(f.Left + 30, f.Top + 30));
        form.CloseRequested += CloseNote;
        form.DeleteRequested += DeleteNote;
        form.SaveRequested += _ => SaveAll(); // Ctrl+S → 立即存檔
        form.Changed += ScheduleSave; // 移動/縮放/內容/顏色變更 → 防抖存檔

        _notes.Add(form);
        if (activate) form.Show();
        else form.ShowWithoutFocus();
        _notesVisible = true;
        UpdateTrayState();
        ScheduleSave();
        return form;
    }

    /// <summary>關閉便箋：保留資料，之後可從系統匣「已關閉的便箋」再開啟</summary>
    private void CloseNote(NoteForm form)
    {
        // 響鈴中按 ✕ 視同「完成」：否則提醒仍到期，15 秒後便箋又會被自動打開
        if (form.IsRinging)
            form.CompleteReminder();

        var data = form.ToData(); // 先把最新狀態寫回資料
        data.IsOpen = false;
        _closedNotes.Add(data);

        DetachAndDispose(form);
        UpdateTrayState();
        SaveAll();
    }

    /// <summary>永久刪除便箋（NoteForm 已做過確認）</summary>
    private void DeleteNote(NoteForm form)
    {
        DetachAndDispose(form);
        UpdateTrayState();
        SaveAll();
    }

    private void DetachAndDispose(NoteForm form)
    {
        _notes.Remove(form);
        form.CloseRequested -= CloseNote; // 解除訂閱後 Dispose，避免 OnFormClosing 再次攔截
        form.DeleteRequested -= DeleteNote;
        form.Dispose();
    }

    /// <summary>重建「已關閉的便箋」子選單（每次開啟系統匣選單時呼叫）</summary>
    private void RebuildClosedMenu()
    {
        _closedMenu.DropDownItems.Clear();
        _closedMenu.Enabled = _closedNotes.Count > 0;

        if (_closedNotes.Count > 0)
        {
            _closedMenu.DropDownItems.Add("全部還原", null, (_, _) => RestoreAllClosed());
            _closedMenu.DropDownItems.Add(new ToolStripSeparator());
        }

        foreach (var data in _closedNotes.ToList())
        {
            var item = new ToolStripMenuItem(data.DisplayName(16));
            item.Click += (_, _) =>
            {
                _closedNotes.Remove(data);
                CreateNote(data); // 會把 IsOpen 設回 true
            };
            _closedMenu.DropDownItems.Add(item);
        }
    }

    /// <summary>重建「即將到來的提醒」子選單：開啟中與已關閉的便箋都列出，依響鈴時間排序</summary>
    private void RebuildReminderMenu()
    {
        _reminderMenu.DropDownItems.Clear();
        var now = DateTime.Now;
        var upcoming = _notes.Select(n => (Form: (NoteForm?)n, Data: n.ToData()))
            .Concat(_closedNotes.Select(d => (Form: (NoteForm?)null, Data: d)))
            .Select(x => (x.Form, x.Data, Due: ReminderSchedule.DueTime(x.Data)))
            .Where(x => x.Due != null)
            .OrderBy(x => x.Due)
            .ToList();

        _reminderMenu.Enabled = upcoming.Count > 0;
        foreach (var (form, data, due) in upcoming)
        {
            string repeat = data.ReminderRepeat == ReminderRepeat.None ? "" : " ↻";
            var item = new ToolStripMenuItem(
                $"{ReminderSchedule.FormatShort(due!.Value, now)}{repeat}　{data.DisplayName(16)}");
            item.Click += (_, _) => OpenNote(form, data);
            _reminderMenu.DropDownItems.Add(item);
        }
    }

    /// <summary>把便箋叫到前景並取得焦點；已關閉的先重新開啟</summary>
    private void OpenNote(NoteForm? form, NoteData data)
    {
        if (form == null || form.IsDisposed)
        {
            if (!_closedNotes.Remove(data))
                return; // 選單開啟後狀態已變（例如已被提醒自動開啟）
            CreateNote(data);
            return;
        }
        form.Show();
        if (form.WindowState == FormWindowState.Minimized)
            form.WindowState = FormWindowState.Normal;
        form.Activate();
    }

    /// <summary>點提醒氣泡：把正在響鈴的便箋叫到前景</summary>
    private void FocusRingingNote()
    {
        if (_notes.FirstOrDefault(n => n.IsRinging) is { } form)
            OpenNote(form, form.ToData());
    }

    /// <summary>
    /// 檢查所有便箋的提醒：開啟中的直接響鈴，已關閉的先重新開啟再響鈴。
    /// 每次都直接比對「現在 ≥ 響鈴時間」，睡眠喚醒或調整系統時間都不會漏。
    /// </summary>
    private void CheckReminders()
    {
        if (_exiting)
            return;

        var now = DateTime.Now;
        var rang = new List<NoteForm>();

        foreach (var form in _notes.ToList())
        {
            if (!form.IsRinging && ReminderSchedule.IsDue(form.ToData(), now))
                rang.Add(form);
        }
        foreach (var data in _closedNotes.Where(d => ReminderSchedule.IsDue(d, now)).ToList())
        {
            _closedNotes.Remove(data);
            rang.Add(CreateNote(data, activate: false));
        }

        foreach (var form in rang)
            form.StartRinging();
        foreach (var form in _notes)
            form.RefreshReminderLabel(); // 跨日後「明天」要改成當天時間

        if (rang.Count > 0)
        {
            string text = rang.Count == 1
                ? rang[0].ToData().DisplayName(16)
                : $"{rang.Count} 張便箋的提醒時間到了";
            _trayIcon.ShowBalloonTip(10_000, "MemoTack 提醒", text, ToolTipIcon.Info);
            UpdateTrayState();
            SaveAll(); // 已關閉的便箋被重新開啟，狀態要立刻寫回
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
            CheckReminders();
    }

    /// <summary>開啟設定視窗；確定後套用到所有便箋並存檔</summary>
    /// <summary>
    /// 檢查更新。自動檢查：設定關閉、沒網路、已略過這個版本時都安靜跳過；
    /// 手動檢查（系統匣選單）：一律告訴使用者結果。發現新版一律先詢問，不會自己安裝。
    /// </summary>
    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (_exiting || _checkingUpdate || (!manual && !_settings.CheckForUpdates))
            return;

        _checkingUpdate = true;
        try
        {
            UpdateInfo? info;
            try
            {
                info = await UpdateChecker.CheckAsync();
            }
            catch (Exception)
            {
                if (manual)
                    MessageBox.Show("無法連線到 GitHub 檢查更新，請確認網路連線後再試。", "MemoTack",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (info == null)
            {
                if (manual)
                    MessageBox.Show($"目前已是最新版本（{UpdateChecker.CurrentVersion.ToString(3)}）。", "MemoTack",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!manual && info.Version.ToString(3) == _settings.SkippedVersion)
                return; // 使用者選過「略過這個版本」；手動檢查時仍會顯示

            using var dialog = new UpdateForm(info);
            dialog.ShowDialog();
            switch (dialog.Result)
            {
                case UpdateForm.Choice.Update when dialog.InstallerPath != null:
                    InstallUpdate(dialog.InstallerPath);
                    break;
                case UpdateForm.Choice.Skip:
                    _settings.SkippedVersion = info.Version.ToString(3);
                    SaveAll();
                    break;
            }
        }
        finally
        {
            _checkingUpdate = false;
        }
    }

    /// <summary>
    /// 先存檔、放開單一實例鎖，再啟動安裝程式並結束 MemoTack；
    /// 安裝程式會等 MemoTack 結束後覆蓋檔案，完成後重新開啟它（installer.iss）。
    /// </summary>
    private void InstallUpdate(string installerPath)
    {
        SaveAll();
        Program.ReleaseSingleInstance();
        try
        {
            UpdateChecker.RunInstaller(installerPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show("無法啟動安裝程式：" + ex.Message, "MemoTack", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return; // 單一實例鎖已放開，但程式照常執行；下次啟動會重新取得
        }
        ExitApp();
    }

    private void OpenSettings()
    {
        float oldContentSize = _settings.ContentFontSize;
        using var dlg = new SettingsForm(_settings);
        if (dlg.ShowDialog() != DialogResult.OK)
            return;

        // 只有內容字級真的改了，才覆蓋各便箋用 Ctrl+滾輪 做的個別調整
        bool contentSizeChanged = Math.Abs(_settings.ContentFontSize - oldContentSize) > 0.01f;
        foreach (var n in _notes)
            n.ApplySettings(resetContentSize: contentSizeChanged);

        ApplyHotkey(); // 快捷鍵可能改了，重新註冊
        SaveAll();
    }

    /// <summary>依設定（重新）註冊所有全域快捷鍵；失敗時以系統匣氣泡提示</summary>
    private void ApplyHotkey()
    {
        _hotkey.UnregisterAll();
        RegisterHotkey(HotkeyToggleId, _settings.Hotkey, "顯示/隱藏所有便箋");
        RegisterHotkey(HotkeyRestoreId, _settings.RestoreHotkey, "還原已關閉便箋");
    }

    private void RegisterHotkey(int id, string combo, string purpose)
    {
        if (string.IsNullOrWhiteSpace(combo))
            return; // 留空 = 停用

        bool ok = HotkeyManager.TryParse(combo, out var mods, out var key)
                  && _hotkey.TryRegister(id, mods, key);
        if (!ok)
        {
            _trayIcon.ShowBalloonTip(3000, "MemoTack",
                $"{purpose}快捷鍵「{combo}」註冊失敗：格式錯誤，或已被系統/其他程式占用。",
                ToolTipIcon.Warning);
        }
    }

    /// <summary>還原所有已關閉的便箋</summary>
    private void RestoreAllClosed()
    {
        if (_closedNotes.Count == 0)
            return;

        foreach (var data in _closedNotes.ToList())
            CreateNote(data); // 會把 IsOpen 設回 true

        _closedNotes.Clear();
        UpdateTrayState();
        SaveAll();
    }

    private void ToggleAllNotes()
    {
        if (_notes.Count == 0)
            return;

        // 自癒式切換：Win+D「顯示桌面」會把便箋最小化，單純 Show() 救不回來。
        // 只要有任何便箋被隱藏或最小化，這次操作一律視為「全部還原顯示」；
        // 全部都正常顯示時，才執行隱藏。
        SetNotesVisible(_notes.Any(n => !n.Visible || n.WindowState == FormWindowState.Minimized));
    }

    /// <summary>
    /// 快捷鍵：便箋全部看得到（顯示中、沒被其他程式的視窗蓋住）時隱藏；否則把便箋叫到最前面。
    /// - 單純依「是否顯示中」切換的話，便箋被蓋住時按下去反而會隱藏，要按兩次才叫得出來。
    /// - 也不能依「前景是不是便箋」判斷：叫出便箋刻意不搶焦點，焦點留在原程式，
    ///   在其他程式裡就永遠隱藏不了。
    /// </summary>
    private void ToggleFromHotkey()
    {
        var foreground = GetForegroundWindow();
        bool onNotes = _notes.Any(n => n.IsHandleCreated && n.Handle == foreground);
        if (onNotes || AllNotesUncovered())
            SetNotesVisible(false);
        else
            ShowAllNotes();
    }

    /// <summary>
    /// 每張便箋都顯示中，而且沒有其他程式的視窗蓋在上面。
    /// 沿 Z 順序往上看：忽略隱藏、最小化、被 DWM 遮蔽（其他虛擬桌面、暫停的 UWP）、
    /// 工具視窗（工作列、提示框、陰影）與滑鼠穿透的覆蓋層，避免被看不見的視窗誤判成「蓋住」。
    /// </summary>
    private bool AllNotesUncovered()
    {
        if (_notes.Count == 0)
            return false;

        var ours = _notes.Where(n => n.IsHandleCreated).Select(n => n.Handle).ToHashSet();
        foreach (var n in _notes)
        {
            if (!n.Visible || n.WindowState == FormWindowState.Minimized || !n.IsHandleCreated)
                return false;

            var rect = n.Bounds;
            for (var h = GetWindow(n.Handle, GW_HWNDPREV); h != IntPtr.Zero; h = GetWindow(h, GW_HWNDPREV))
            {
                if (ours.Contains(h) || !IsWindowVisible(h) || IsIconic(h) || IsCloaked(h))
                    continue;
                long ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
                if ((ex & (WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT)) != 0)
                    continue;
                if (GetWindowRect(h, out var r) &&
                    Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom).IntersectsWith(rect))
                    return false;
            }
        }
        return true;
    }

    private static bool IsCloaked(IntPtr hwnd) =>
        DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0;

    private const uint GW_HWNDPREV = 3;
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x80, WS_EX_TRANSPARENT = 0x20;
    private const int DWMWA_CLOAKED = 14;

    private struct RECT { public int Left, Top, Right, Bottom; }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);

    /// <summary>
    /// 把所有便箋叫到最前面（單擊系統匣、快捷鍵、再次開啟 MemoTack 時）：
    /// 隱藏的顯示、最小化的還原、被蓋住的浮到上方。沒有開啟中的便箋時新增一張。
    /// </summary>
    public void ShowAllNotes()
    {
        if (_exiting)
            return;
        if (_notes.Count == 0)
            CreateNote(null);
        else
            SetNotesVisible(true);
    }

    private void SetNotesVisible(bool show)
    {
        _notesVisible = show;

        foreach (var n in _notes)
        {
            if (show)
            {
                n.Show();
                if (n.WindowState == FormWindowState.Minimized)
                    n.WindowState = FormWindowState.Normal; // 解除 Win+D 造成的最小化
                n.RaiseToTop(); // 浮到其他程式的視窗上方（BringToFront 只在同程式內有效）
            }
            else
            {
                n.Hide();
            }
        }
        UpdateTrayState();
    }

    // ---------- 存檔與結束 ----------

    /// <summary>防抖：任何變更後重新計時，靜止 1.5 秒才真正寫檔</summary>
    private void ScheduleSave()
    {
        if (_exiting) return; // 結束流程中，計時器已釋放
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveAll()
    {
        // 開啟中的便箋 + 已關閉保留的便箋一起存
        var all = _notes.Select(n => n.ToData()).Concat(_closedNotes).ToList();
        NoteStorage.Save(new AppState
        {
            Settings = _settings,
            Notes = all,
        });
    }

    private void OnSessionEnding(object? sender, SessionEndingEventArgs e) => SaveAll();

    /// <summary>
    /// 未預期的 UI 例外：先搶救存檔再提示，程式繼續執行，不讓一個 bug 帶走未存的內容。
    /// </summary>
    public void HandleUnhandledException(Exception ex)
    {
        try
        {
            if (!_exiting) SaveAll();
        }
        catch
        {
            // 狀態已經不正常，存不了就算了，至少把錯誤告訴使用者
        }
        MessageBox.Show($"MemoTack 發生未預期的錯誤，已嘗試存檔。\n\n{ex.Message}",
            "MemoTack", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void ExitApp()
    {
        _exiting = true;
        _hotkey.Dispose();
        _saveTimer.Stop();
        _saveTimer.Dispose();
        _reminderTimer.Stop();
        _reminderTimer.Dispose();
        _updateTimer.Stop();
        _updateTimer.Dispose();
        SaveAll();

        SystemEvents.SessionEnding -= OnSessionEnding;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _trayIcon.Visible = false; // 先隱藏，避免殘影留在系統匣
        _trayIcon.Dispose();

        foreach (var n in _notes.ToList())
            n.Dispose();

        ExitThread(); // 結束訊息迴圈
    }

    // ---------- 系統匣圖示（GDI+ 動態繪製，免外部 .ico 檔） ----------

    /// <param name="hidden">true = 灰色（便箋隱藏中），false = 黃色（正常）</param>
    private static Icon CreateTrayIcon(bool hidden)
    {
        Color bodyColor = hidden ? Color.FromArgb(168, 168, 168) : Color.FromArgb(255, 222, 89);
        Color foldColor = hidden ? Color.FromArgb(120, 120, 120) : Color.FromArgb(214, 176, 40);

        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            using var body = new SolidBrush(bodyColor);
            using var fold = new SolidBrush(foldColor);
            g.FillRectangle(body, 1, 1, 14, 14);                      // 便利貼本體
            g.FillPolygon(fold, new[]                                  // 右下角摺角
            {
                new Point(10, 15), new Point(15, 10), new Point(15, 15)
            });
        }
        return Icon.FromHandle(bmp.GetHicon());
    }
}
