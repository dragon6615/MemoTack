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

        // 舊版預設快捷鍵遷移：
        // - Ctrl+Alt+S 是更早的預設值
        // - Alt+F12 不會生效（F12 被 Windows 保留給除錯器）
        if (_settings.Hotkey is "Ctrl+Alt+S" or "Alt+F12")
            _settings.Hotkey = "Alt+F10";

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

        menu.Items.Add("設定...", null, (_, _) => OpenSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("結束", null, (_, _) => ExitApp());
        menu.Opening += (_, _) =>
        {
            RebuildClosedMenu();
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
        _trayIcon.DoubleClick += (_, _) => ToggleAllNotes();

        // Windows 關機/登出時搶先存檔
        SystemEvents.SessionEnding += OnSessionEnding;

        // ---- 全域快捷鍵 ----
        _hotkey = new HotkeyManager();
        _hotkey.Pressed += id =>
        {
            if (id == HotkeyToggleId) ToggleAllNotes();
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
        if (hidden) text += "（隱藏中，雙擊顯示）";
        _trayIcon.Text = text.Length <= 63 ? text : text[..63]; // NotifyIcon.Text 長度上限保護
    }

    // ---------- 便箋管理 ----------

    /// <summary>
    /// 建立一張便箋並顯示。data 為 null 時建立新便箋（可指定位置）。
    /// </summary>
    private void CreateNote(NoteData? data, Point? location = null)
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
        form.Show();
        _notesVisible = true;
        UpdateTrayState();
        ScheduleSave();
    }

    /// <summary>關閉便箋：保留資料，之後可從系統匣「已關閉的便箋」再開啟</summary>
    private void CloseNote(NoteForm form)
    {
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
            var item = new ToolStripMenuItem(MakePreview(data.Content));
            item.Click += (_, _) =>
            {
                _closedNotes.Remove(data);
                CreateNote(data); // 會把 IsOpen 設回 true
            };
            _closedMenu.DropDownItems.Add(item);
        }
    }

    /// <summary>取內容第一行前 16 字當選單預覽文字</summary>
    private static string MakePreview(string content)
    {
        string firstLine = content.Split('\n', '\r').FirstOrDefault(s => s.Trim().Length > 0)?.Trim() ?? "";
        if (firstLine.Length == 0) return "（空白便箋）";
        return firstLine.Length <= 16 ? firstLine : firstLine[..16] + "…";
    }

    /// <summary>開啟設定視窗；確定後套用到所有便箋並存檔</summary>
    private void OpenSettings()
    {
        using var dlg = new SettingsForm(_settings);
        if (dlg.ShowDialog() != DialogResult.OK)
            return;

        foreach (var n in _notes)
            n.ApplySettings(resetContentSize: true); // 內容字級重設為新設定值

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
        bool show = _notes.Any(n => !n.Visible || n.WindowState == FormWindowState.Minimized);
        _notesVisible = show;

        foreach (var n in _notes)
        {
            if (show)
            {
                n.Show();
                if (n.WindowState == FormWindowState.Minimized)
                    n.WindowState = FormWindowState.Normal; // 解除 Win+D 造成的最小化
                n.BringToFront(); // 非置頂模式時把便箋帶到前景
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

    private void ExitApp()
    {
        _exiting = true;
        _hotkey.Dispose();
        _saveTimer.Stop();
        _saveTimer.Dispose();
        SaveAll();

        SystemEvents.SessionEnding -= OnSessionEnding;
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
