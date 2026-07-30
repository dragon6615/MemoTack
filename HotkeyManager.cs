using System.Runtime.InteropServices;

namespace MemoTack;

/// <summary>
/// 全域快捷鍵管理：建立一個隱藏的訊息視窗接收 WM_HOTKEY。
/// 注意：RegisterHotKey 是「先註冊先贏」，被系統或其他程式占用的組合
/// （例如 Win+S 是 Windows 搜尋）會註冊失敗。
/// </summary>
public sealed class HotkeyManager : NativeWindow, IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [Flags]
    public enum Mods : uint
    {
        None = 0,
        Alt = 0x0001,
        Ctrl = 0x0002,
        Shift = 0x0004,
        Win = 0x0008,
        NoRepeat = 0x4000, // 按住不放不要連發
    }

    /// <summary>快捷鍵被按下，參數為註冊時的 id</summary>
    public event Action<int>? Pressed;

    private readonly HashSet<int> _registered = new();

    public HotkeyManager()
    {
        CreateHandle(new CreateParams()); // 隱藏訊息視窗
    }

    /// <summary>嘗試註冊快捷鍵（同一 id 會先解除舊的）；組合被占用或無效時回傳 false</summary>
    public bool TryRegister(int id, Mods mods, Keys key)
    {
        Unregister(id);
        if (RegisterHotKey(Handle, id, (uint)(mods | Mods.NoRepeat), (uint)key))
        {
            _registered.Add(id);
            return true;
        }
        return false;
    }

    public void Unregister(int id)
    {
        if (_registered.Remove(id))
            UnregisterHotKey(Handle, id);
    }

    public void UnregisterAll()
    {
        foreach (int id in _registered)
            UnregisterHotKey(Handle, id);
        _registered.Clear();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
            Pressed?.Invoke((int)m.WParam);
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterAll();
        DestroyHandle();
    }

    /// <summary>
    /// 解析 "Ctrl+Alt+S"、"Win+Shift+N" 這類字串。
    /// 至少要有一個修飾鍵；主鍵支援 A-Z、0-9、F1-F12 等 Keys 列舉名稱。
    /// </summary>
    public static bool TryParse(string text, out Mods mods, out Keys key)
    {
        mods = Mods.None;
        key = Keys.None;

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            return false;

        // 前面的都是修飾鍵，最後一個是主鍵
        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= Mods.Ctrl; break;
                case "alt": mods |= Mods.Alt; break;
                case "shift": mods |= Mods.Shift; break;
                case "win" or "windows": mods |= Mods.Win; break;
                default: return false;
            }
        }
        if (mods == Mods.None)
            return false;

        string keyName = parts[^1];
        if (keyName.Length == 1 && char.IsDigit(keyName[0]))
            keyName = "D" + keyName; // Keys 列舉的數字鍵是 D0-D9

        if (!Enum.TryParse(keyName, ignoreCase: true, out key) || key == Keys.None)
            return false;

        // F12 被 Windows 保留給除錯器：RegisterHotKey 會成功，但按鍵事件被系統攔走，
        // 實際上永遠不會觸發，因此直接視為無效
        if (key == Keys.F12)
            return false;

        return true;
    }
}
