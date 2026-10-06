using System.Runtime.InteropServices;

namespace MemoTack;

/// <summary>
/// 便箋內容編輯器：在 RichTextBox 上即時套用 markdown 樣式，文字本身仍是純文字 markdown。
///
/// 幾個刻意的設計：
/// - 只重新套用變動的那幾行（以 \n 分段的「段落」，不是自動換行後的顯示行），長便箋打字也不卡。
/// - 輸入法組字中完全不碰格式：改選取範圍會讓注音／倉頡的組字被中斷，等組字結束再補。
/// - 套用格式時暫停復原記錄（TOM tomSuspend），Ctrl+Z 只會復原打的字，不會先復原格式。
/// - 貼上一律轉成純文字，外部的字型顏色不會混進來；RichEdit 內建的對齊、字級等格式快捷鍵一律攔下。
/// </summary>
public sealed class MarkdownTextBox : RichTextBox
{
    private static readonly float[] HeadingScale = { 1f, 1.5f, 1.3f, 1.15f };

    private readonly Dictionary<(string Family, float Size, FontStyle Style), Font> _fonts = new();
    private string _text = string.Empty; // Text 的快取：Text 屬性每次都會跟控制項要整份字串
    private bool _formatting;
    private bool _composing;              // 輸入法組字中（WM_IME_STARTCOMPOSITION ~ ENDCOMPOSITION）
    private bool _dirtyWhileComposing;    // 組字期間文字有變，結束後要補格式
    private bool _fullPending = true;     // 下次文字變更時整篇重新套用（貼上、復原、載入）
    private ITextDocument? _tom;

    public MarkdownTextBox()
    {
        Multiline = true;
        WordWrap = true;
        AcceptsTab = true;
        DetectUrls = false;
        ScrollBars = RichTextBoxScrollBars.None;
        BorderStyle = BorderStyle.None;
        ContextMenuStrip = BuildContextMenu(); // RichTextBox 沒有內建右鍵選單，補回 TextBox 原本有的
    }

    // ---------- 套用格式 ----------

    /// <summary>程式直接設定內容（載入便箋等）可能一次換掉多行，整篇重新套用</summary>
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text
    {
        get => base.Text;
        set
        {
            _fullPending = true;
            base.Text = value;
        }
    }

    /// <summary>
    /// 存檔用的標準 markdown 內容。編輯器畫面上待辦顯示為 ☐／☑，
    /// 讀寫時與「- [ ]」「- [x]」互轉；載入與存檔請用這個，不要直接用 Text。
    /// </summary>
    public string Markdown
    {
        get => MarkdownSyntax.ToStorage(Text);
        set => Text = MarkdownSyntax.ToEditor(value ?? string.Empty);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _tom = GetTextDocument();
        _text = Text;
        FormatAll();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (_tom != null)
        {
            Marshal.ReleaseComObject(_tom);
            _tom = null;
        }
        base.OnHandleDestroyed(e);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        if (_formatting)
            return; // 套用格式不會改文字，這裡只是保險
        _text = Text;

        if (_composing || IsImeComposing())
            _dirtyWhileComposing = true;
        else
            FormatChanged();

        base.OnTextChanged(e);
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e); // 設定 Font 會把整篇改成同一個字型，要重新套用
        ClearFontCache();
        FormatAll();
    }

    protected override void OnForeColorChanged(EventArgs e)
    {
        base.OnForeColorChanged(e);
        FormatAll(); // 符號與完成項目的顏色由前景／背景色混合而來
    }

    protected override void OnBackColorChanged(EventArgs e)
    {
        base.OnBackColorChanged(e);
        FormatAll();
    }

    /// <summary>
    /// 文字變更後重新套用：游標所在段落與前一段（Enter 會把一段拆成兩段、Backspace 會合併兩段）。
    /// 貼上、復原等可能大範圍變動的操作會事先設 _fullPending，改為整篇重新套用。
    /// </summary>
    private void FormatChanged()
    {
        if (_fullPending)
        {
            FormatAll();
            return;
        }
        int caret = SelectionStart;
        int prev = ParagraphAt(caret).Start - 1;
        Format(Math.Max(0, prev), caret);
        SetActive(ActiveRange()); // 按 Enter 換到下一行時，上一行就離開了

        // 不能在文字變更通知裡同步再改文字（RichEdit 還在處理這次變更，會只換掉一部分），排到之後再做
        if (TypedPrefixAt(caret) != null)
            BeginInvoke(ConvertTypedPrefix);
    }

    /// <summary>游標正好在行首待辦（「- [ ] 」「[] 」…）或清單（「- 」…）寫法的空白之後時，回傳要取代的範圍與 ☐／☑／• 前綴</summary>
    private (int Start, int Length, string Replacement)? TypedPrefixAt(int caret)
    {
        var (start, length) = ParagraphAt(caret);
        if (MarkdownSyntax.TypedPrefix(_text.Substring(start, length)) is not { } typed || caret != start + typed.Length)
            return null;
        return (start, typed.Length, typed.Replacement);
    }

    /// <summary>把剛打完的待辦／清單寫法換成 ☐／☑／•。是一般的文字編輯，可以用 Ctrl+Z 還原成原本打的字</summary>
    private void ConvertTypedPrefix()
    {
        if (IsDisposed || SelectionLength > 0 || TypedPrefixAt(SelectionStart) is not { } typed)
            return; // 排程期間使用者又打了字或移動游標，就不轉
        Select(typed.Start, typed.Length);
        SelectedText = typed.Replacement; // 游標會落在 ☐ 後面
    }

    private void FormatAll()
    {
        Format(0, _text.Length);
        _fullPending = false;
        _active = ActiveRange();
    }

    // ---------- 目前行顯示符號、其他行隱藏 ----------

    private (int Start, int End) _active = (-1, -1); // 目前顯示符號的段落範圍（字元位置）
    private bool _activePending;                     // 選取中（拖曳／Shift）延後更新

    /// <summary>
    /// 要顯示 markdown 符號的段落範圍：游標或選取範圍所在的段落。沒有焦點時沒有（全部隱藏）。
    /// 跟 Obsidian、Typora 一樣：正在編輯的行看得到符號可以直接改，其他行只看到排版結果。
    /// </summary>
    private (int Start, int End) ActiveRange() => ActiveRange(SelectionStart, SelectionLength);

    private (int Start, int End) ActiveRange(int selStart, int selLength)
    {
        if (!Focused)
            return (-1, -1);
        var first = ParagraphAt(selStart);
        var last = ParagraphAt(selStart + selLength);
        return (first.Start, last.Start + last.Length);
    }

    /// <summary>游標換到別的段落時：舊段落隱藏符號、新段落顯示符號</summary>
    private void UpdateActive()
    {
        _activePending = false;
        if (_formatting || _composing || !IsHandleCreated)
            return;
        var now = ActiveRange();
        if (now == _active)
            return;
        var old = _active;
        SetActive(now);
        if (old.Start >= 0)
            Format(Math.Min(old.Start, _text.Length), Math.Min(old.End, _text.Length));
        if (now.Start >= 0)
            Format(now.Start, now.End);
    }

    /// <summary>更新目前段落；有段落被離開時，排程檢查是否有沒轉換到的清單／待辦寫法</summary>
    private void SetActive((int Start, int End) now)
    {
        if (now == _active)
            return;
        bool left = _active.Start >= 0;
        _active = now;
        if (left && !_convertScheduled && !_converting)
        {
            _convertScheduled = true;
            BeginInvoke(ConvertLeftoverPrefixes);
        }
    }

    private bool _convertScheduled;
    private bool _converting;

    /// <summary>
    /// 保險：自動轉換只在「行首剛打完『- 』且游標就在空白後」那一刻觸發，打字順序不同
    /// （例如先打空白再回頭補 -）就不會轉，畫面會留著「- 」，重開便箋後卻變成「•」。
    /// 所以游標離開的行，若仍是「- 」「* 」「- [ ]」寫法，就在這裡補轉成 •／☐，跟載入時的結果一致。
    /// </summary>
    private void ConvertLeftoverPrefixes()
    {
        _convertScheduled = false;
        if (ReadOnly || IsDisposed || !IsHandleCreated || _formatting || _composing || IsImeComposing())
            return;

        var active = ActiveRange();
        int selStart = SelectionStart, selLength = SelectionLength;
        var paragraphs = new List<(int Start, int Length)>();
        for (int pos = 0; pos <= _text.Length;)
        {
            int end = _text.IndexOf('\n', pos);
            if (end < 0) end = _text.Length;
            paragraphs.Add((pos, end - pos));
            pos = end + 1;
        }

        _converting = true;
        try
        {
            // 從後往前改，前面段落的位置才不會被影響
            for (int i = paragraphs.Count - 1; i >= 0; i--)
            {
                var (start, length) = paragraphs[i];
                if (start >= active.Start && start <= active.End)
                    continue; // 正在編輯的行不動
                string line = _text.Substring(start, length);
                string converted = MarkdownSyntax.ToEditor(line);
                if (converted == line)
                    continue;

                // ToEditor 只改行首前綴：去掉相同的結尾，剩下的就是新舊前綴
                int same = 0;
                while (same < line.Length && same < converted.Length &&
                       line[^(same + 1)] == converted[^(same + 1)])
                    same++;
                int oldLength = line.Length - same;
                string newPrefix = converted[..(converted.Length - same)];

                Select(start, oldLength);
                SelectedText = newPrefix; // 一般的文字編輯，可以 Ctrl+Z 還原
                if (start < selStart)
                    selStart += newPrefix.Length - oldLength;
            }
        }
        finally
        {
            Select(selStart, selLength);
            _converting = false;
        }
        UpdateActive(); // 轉換過程中選取範圍有變動，重新對齊目前段落的符號顯示
    }

    protected override void OnSelectionChanged(EventArgs e)
    {
        base.OnSelectionChanged(e);
        if (_formatting || _converting)
            return;
        // 拖曳或 Shift 選取中重新套用格式會改動選取範圍、打斷選取，等放開再做
        if (MouseButtons != MouseButtons.None || (ModifierKeys & Keys.Shift) != 0)
            _activePending = true;
        else
            UpdateActive();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_activePending)
            UpdateActive();
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (_activePending && (ModifierKeys & Keys.Shift) == 0)
            UpdateActive();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        UpdateActive();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        UpdateActive(); // 沒有焦點時全部隱藏
    }

    /// <summary>重新套用涵蓋 [from, to] 的所有段落；保留選取範圍與捲動位置，不重繪、不記入復原</summary>
    private void Format(int from, int to)
    {
        if (!IsHandleCreated || _formatting)
            return;

        _formatting = true;
        int selStart = SelectionStart, selLength = SelectionLength;
        var active = ActiveRange(selStart, selLength);
        var scroll = GetScrollPos();
        SendMessage(Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
        _tom?.Undo(TomSuspend, out _);
        try
        {
            int pos = ParagraphAt(Math.Min(from, _text.Length)).Start;
            while (true)
            {
                int end = _text.IndexOf('\n', pos);
                if (end < 0) end = _text.Length;
                FormatParagraph(pos, _text.Substring(pos, end - pos), showMarkers: pos >= active.Start && pos <= active.End);
                if (end >= to || end >= _text.Length)
                    break;
                pos = end + 1;
            }
        }
        finally
        {
            _tom?.Undo(TomResume, out _);
            Select(selStart, selLength);
            SetScrollPos(scroll);
            SendMessage(Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
            Invalidate();
            _formatting = false;
        }
    }

    /// <param name="showMarkers">true = 正在編輯的段落：符號變淡顯示；false = 符號隱藏（仍在文字裡，只是不畫）</param>
    private void FormatParagraph(int start, string line, bool showMarkers)
    {
        var md = MarkdownSyntax.Analyze(line);
        float size = Font.Size * HeadingScale[md.HeadingLevel];
        var baseStyle = md.HeadingLevel > 0 ? FontStyle.Bold : FontStyle.Regular;

        // 先整段還原成基本樣式，再逐段套用（空段落也要做：決定在這裡打字時的字型）
        Select(start, line.Length);
        SelectionFont = FontFor(size, baseStyle);
        SelectionColor = ForeColor;
        SetSelectionHidden(false);

        foreach (var (runStart, length, style) in md.Runs())
        {
            if (style == MdStyle.None)
                continue;

            var fontStyle = baseStyle;
            if ((style & (MdStyle.Bold | MdStyle.Checkbox)) != 0) fontStyle |= FontStyle.Bold;
            if (style.HasFlag(MdStyle.Italic)) fontStyle |= FontStyle.Italic;
            if ((style & (MdStyle.Strike | MdStyle.Done)) != 0) fontStyle |= FontStyle.Strikeout;

            Select(start + runStart, length);
            SelectionFont = style.HasFlag(MdStyle.Checkbox) && md.CheckboxLength == 1
                ? CheckboxFont(size)
                : FontFor(size, fontStyle);
            SelectionColor = style.HasFlag(MdStyle.Marker) ? Blend(ForeColor, BackColor, 0.55f)
                : style.HasFlag(MdStyle.Done) ? Blend(ForeColor, BackColor, 0.45f)
                : ForeColor;
            if (style.HasFlag(MdStyle.Marker) && !showMarkers)
                SetSelectionHidden(true);
        }
    }

    /// <summary>RichEdit 隱藏文字（CFE_HIDDEN）：字元仍在內容裡、存檔與複製都在，只是不顯示也不佔版面</summary>
    private void SetSelectionHidden(bool hidden)
    {
        var format = new CHARFORMAT2
        {
            cbSize = Marshal.SizeOf<CHARFORMAT2>(),
            dwMask = CFM_HIDDEN,
            dwEffects = hidden ? CFE_HIDDEN : 0,
            szFaceName = string.Empty,
        };
        SendMessage(Handle, EM_SETCHARFORMAT, (IntPtr)SCF_SELECTION, ref format);
    }

    private Font FontFor(float size, FontStyle style) => CachedFont(Font.FontFamily.Name, size, style);

    /// <summary>☐／☑ 用 Segoe UI Symbol 畫：內容字型（例如微軟正黑體）裡的方框大小、粗細不一，看起來不像勾選框</summary>
    private Font CheckboxFont(float size) => CachedFont("Segoe UI Symbol", size * 1.1f, FontStyle.Regular);

    private Font CachedFont(string family, float size, FontStyle style)
    {
        if (!_fonts.TryGetValue((family, size, style), out var font))
        {
            font = new Font(family, size, style);
            _fonts[(family, size, style)] = font;
        }
        return font;
    }

    /// <summary>RichEdit 只複製字型屬性、不持有 HFONT，所以換字型時可以直接釋放快取</summary>
    private void ClearFontCache()
    {
        foreach (var f in _fonts.Values)
            f.Dispose();
        _fonts.Clear();
    }

    private static Color Blend(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    /// <summary>index 所在段落（以 \n 分隔）的起點與長度</summary>
    private (int Start, int Length) ParagraphAt(int index)
    {
        index = Math.Clamp(index, 0, _text.Length);
        int start = index == 0 ? 0 : _text.LastIndexOf('\n', index - 1) + 1;
        int end = _text.IndexOf('\n', index);
        if (end < 0) end = _text.Length;
        return (start, end - start);
    }

    // ---------- 輸入法 ----------

    /// <summary>
    /// 雙重判斷組字中：WM_IME_* 訊息之外，再問一次 IMM 目前有沒有組字字串，
    /// 避免某些輸入法沒送開始組字訊息時，套用格式把組字打斷。
    /// </summary>
    private bool IsImeComposing()
    {
        var context = ImmGetContext(Handle);
        if (context == IntPtr.Zero)
            return false;
        try
        {
            return ImmGetCompositionStringW(context, GCS_COMPSTR, IntPtr.Zero, 0) > 0;
        }
        finally
        {
            ImmReleaseContext(Handle, context);
        }
    }

    // ---------- 待辦勾選框 ----------

    /// <summary>點在某個待辦的勾選框（☐／☑ 或「[ ]」）上時回傳勾選框所在段落的解析結果與位置，否則 null</summary>
    private (int Position, MdLine Line)? CheckboxAt(Point p)
    {
        if (_text.Length == 0)
            return null;

        int index = GetCharIndexFromPosition(p);
        var (start, length) = ParagraphAt(index);
        var md = MarkdownSyntax.Analyze(_text.Substring(start, length));
        if (md.CheckboxStart < 0)
            return null;

        int checkbox = start + md.CheckboxStart;
        var font = md.CheckboxLength == 1 ? CheckboxFont(Font.Size) : FontFor(Font.Size, FontStyle.Bold);
        var topLeft = GetPositionFromCharIndex(checkbox);
        int width = TextRenderer.MeasureText(_text.Substring(checkbox, md.CheckboxLength), font, Size.Empty, TextFormatFlags.NoPadding).Width;
        bool hit = p.X >= topLeft.X - 2 && p.X <= topLeft.X + width + 2 &&
                   p.Y >= topLeft.Y && p.Y < topLeft.Y + font.Height;
        return hit ? (checkbox, md) : null;
    }

    /// <summary>切換勾選狀態：☐ ↔ ☑（「[ ]」寫法則只改中間那個字）。是一般的文字編輯，可以 Ctrl+Z 復原</summary>
    private bool TryToggleCheckbox(Point p)
    {
        if (ReadOnly || ModifierKeys != Keys.None || CheckboxAt(p) is not { } hit)
            return false;

        Focus();
        int selStart = SelectionStart, selLength = SelectionLength;
        if (hit.Line.CheckboxLength == 1)
        {
            Select(hit.Position, 1);
            SelectedText = (hit.Line.TaskDone ? MarkdownSyntax.Unchecked : MarkdownSyntax.Checked).ToString();
        }
        else
        {
            Select(hit.Position + 1, 1);
            SelectedText = hit.Line.TaskDone ? " " : "x";
        }
        Select(selStart, selLength); // 取代會觸發 TextChanged → 重新套用這一行（完成項目變灰加刪除線）
        return true;
    }

    // ---------- 鍵盤 ----------

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // 唯讀（例如更新說明）：不做任何會改文字的自訂操作；RichEdit 內建的編輯鍵本身會被唯讀擋下
        if (ReadOnly)
            return IsRichEditFormattingShortcut(keyData) || base.ProcessCmdKey(ref msg, keyData);

        switch (keyData)
        {
            case Keys.Control | Keys.V:
            case Keys.Shift | Keys.Insert:
                PastePlainText();
                return true;

            case Keys.Control | Keys.Z:
            case Keys.Control | Keys.Y:
                _fullPending = true; // 交給 RichEdit 復原，之後整篇重新套用
                break;

            case Keys.Control | Keys.B:
                ToggleWrap("**");
                return true;

            case Keys.Control | Keys.I:
                ToggleWrap("*");
                return true;

            case Keys.Enter:
                if (ContinueList())
                    return true;
                break;
        }

        if (IsRichEditFormattingShortcut(keyData))
            return true; // 吃掉：這些會套用 markdown 以外的格式（對齊、底線、字級…）

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private static bool IsRichEditFormattingShortcut(Keys keyData) => keyData switch
    {
        Keys.Control | Keys.L or Keys.Control | Keys.E or Keys.Control | Keys.R or Keys.Control | Keys.J => true, // 對齊
        Keys.Control | Keys.D1 or Keys.Control | Keys.D2 or Keys.Control | Keys.D5 => true,                      // 行距
        Keys.Control | Keys.U => true,                                                                            // 底線
        Keys.Control | Keys.Shift | Keys.L or Keys.Control | Keys.Shift | Keys.A => true,                         // 項目符號、全大寫
        Keys.Control | Keys.Shift | Keys.Oemcomma or Keys.Control | Keys.Shift | Keys.OemPeriod => true,          // 字級
        Keys.Control | Keys.Oemplus or Keys.Control | Keys.Shift | Keys.Oemplus => true,                          // 上下標
        _ => false,
    };

    /// <summary>
    /// 在清單或待辦項目上按 Enter：下一行自動加上相同前綴（編號 +1、待辦一律未勾選）；
    /// 在空的項目上按 Enter 則清掉前綴，結束清單。游標在前綴之前時照常換行。
    /// </summary>
    private bool ContinueList()
    {
        if (SelectionLength > 0)
            return false;

        int caret = SelectionStart;
        var (start, length) = ParagraphAt(caret);
        if (MarkdownSyntax.Continue(_text.Substring(start, length)) is not { } next || caret - start < next.PrefixLength)
            return false;

        if (next.IsEmptyItem)
        {
            if (caret != start + length)
                return false;
            Select(start, length);
            SelectedText = string.Empty;
            return true;
        }

        SelectedText = "\n" + MarkdownSyntax.ToEditor(next.NextPrefix); // 待辦直接插入 ☐
        return true;
    }

    /// <summary>Ctrl+B / Ctrl+I：選取文字前後加上符號；已被同樣符號包住時則移除（一次操作 = 一個復原步驟）</summary>
    private void ToggleWrap(string marker)
    {
        int s = SelectionStart, l = SelectionLength, m = marker.Length;
        if (IsWrapped(_text, s, l, marker))
        {
            string inner = _text.Substring(s, l);
            Select(s - m, l + 2 * m);
            SelectedText = inner;
            Select(s - m, l);
        }
        else
        {
            SelectedText = marker + _text.Substring(s, l) + marker;
            Select(s + m, l);
        }
    }

    /// <summary>斜體看前後各 1 或 3 個星號（3 = 粗斜體）；粗體看 2 個以上；刪除線看前後是否為 ~~</summary>
    private static bool IsWrapped(string text, int s, int l, string marker)
    {
        if (s < marker.Length || s + l + marker.Length > text.Length)
            return false;
        if (marker == "~~")
            return text.Substring(s - 2, 2) == "~~" && text.Substring(s + l, 2) == "~~";
        int before = 0, after = 0;
        for (int i = s - 1; i >= 0 && text[i] == '*'; i--) before++;
        for (int i = s + l; i < text.Length && text[i] == '*'; i++) after++;
        return marker == "*"
            ? before is 1 or 3 && after is 1 or 3
            : before >= 2 && after >= 2;
    }

    /// <summary>
    /// 把游標所在行（或選取範圍涵蓋的所有行）換成指定的整行格式：保留縮排、換掉舊前綴。
    /// 全部已經是這個格式時再選一次 = 取消（變回一般文字）。編號清單依序編 1、2、3…；
    /// 多行時略過空行。整段一次取代，一個 Ctrl+Z 就能復原。
    /// </summary>
    private void ApplyLineKind(LineKind kind)
    {
        int selStart = SelectionStart, selEnd = SelectionStart + SelectionLength;
        var first = ParagraphAt(selStart);
        var last = ParagraphAt(selEnd);
        int blockStart = first.Start, blockLength = last.Start + last.Length - first.Start;
        string[] lines = _text.Substring(blockStart, blockLength).Split('\n');

        bool multi = lines.Length > 1;
        bool allSame = lines.Where(l => !multi || l.Trim().Length > 0)
                            .All(l => MarkdownSyntax.LinePrefix(l).Kind == kind);
        var target = allSame ? LineKind.Text : kind;

        int number = 0;
        var result = lines.Select(line =>
        {
            if (multi && line.Trim().Length == 0)
                return line;
            var (_, prefixLength, indent) = MarkdownSyntax.LinePrefix(line);
            string body = line[prefixLength..];
            string prefix = target switch
            {
                LineKind.Heading1 => "# ",
                LineKind.Heading2 => "## ",
                LineKind.Heading3 => "### ",
                LineKind.Bullet => $"{indent}{MarkdownSyntax.Bullet} ",
                LineKind.Numbered => $"{indent}{++number}. ",
                LineKind.Task => $"{indent}{MarkdownSyntax.Unchecked} ",
                _ => indent,
            };
            return prefix + body;
        });
        string replacement = string.Join("\n", result);

        _fullPending = true; // 可能一次改多行
        Select(blockStart, blockLength);
        SelectedText = replacement;
        if (multi || selEnd > selStart)
            Select(blockStart, replacement.Length); // 保持選取，方便接著換別的格式
        else
            Select(blockStart + replacement.Length, 0);
    }

    // ---------- 剪貼簿與右鍵選單 ----------

    /// <summary>貼上一律用純文字：從網頁、Word 複製來的字型與顏色不會混進便箋</summary>
    private void PastePlainText()
    {
        string text;
        try
        {
            if (!Clipboard.ContainsText())
                return;
            text = Clipboard.GetText(TextDataFormat.UnicodeText);
        }
        catch (ExternalException)
        {
            return; // 剪貼簿被其他程式鎖住
        }

        _fullPending = true; // 可能一次貼進多行
        // 貼進來的「- [ ]」也換成 ☐（剪貼簿裡多半是別處複製來的標準 markdown）
        SelectedText = MarkdownSyntax.ToEditor(text.Replace("\r\n", "\n").Replace('\r', '\n'));
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();
        var undo = new ToolStripMenuItem("復原", null, (_, _) => { _fullPending = true; Undo(); });
        var cut = new ToolStripMenuItem("剪下", null, (_, _) => Cut());
        var copy = new ToolStripMenuItem("複製", null, (_, _) => Copy());
        var paste = new ToolStripMenuItem("貼上", null, (_, _) => PastePlainText());
        var delete = new ToolStripMenuItem("刪除", null, (_, _) => SelectedText = string.Empty);
        var selectAll = new ToolStripMenuItem("全選", null, (_, _) => SelectAll());
        var (format, lineItems) = BuildFormatMenu();
        menu.Items.AddRange(new ToolStripItem[]
        {
            format, new ToolStripSeparator(),
            undo, new ToolStripSeparator(), cut, copy, paste, delete, new ToolStripSeparator(), selectAll,
        });
        menu.Opening += (_, _) =>
        {
            undo.Enabled = CanUndo;
            cut.Enabled = copy.Enabled = delete.Enabled = SelectionLength > 0;
            try { paste.Enabled = Clipboard.ContainsText(); }
            catch (ExternalException) { paste.Enabled = false; }

            // 在目前這行的格式前打勾
            var (start, length) = ParagraphAt(SelectionStart);
            var current = MarkdownSyntax.LinePrefix(_text.Substring(start, length)).Kind;
            foreach (var (kind, item) in lineItems)
                item.Checked = kind == current;
        };
        return menu;
    }

    /// <summary>
    /// 「Markdown 格式」子選單：不熟語法也能用。每一項右側同時列出對應的寫法，用選單也順便學會怎麼打。
    /// </summary>
    private (ToolStripMenuItem Menu, List<(LineKind Kind, ToolStripMenuItem Item)> LineItems) BuildFormatMenu()
    {
        var format = new ToolStripMenuItem("Markdown 格式");
        var lineItems = new List<(LineKind, ToolStripMenuItem)>();

        ToolStripMenuItem Line(string text, string syntax, LineKind kind)
        {
            var item = new ToolStripMenuItem(text, null, (_, _) => ApplyLineKind(kind)) { ShortcutKeyDisplayString = syntax };
            lineItems.Add((kind, item));
            return item;
        }

        ToolStripMenuItem Inline(string text, string syntax, string marker) =>
            new(text, null, (_, _) => ToggleWrap(marker)) { ShortcutKeyDisplayString = syntax };

        format.DropDownItems.AddRange(new ToolStripItem[]
        {
            Line("標題 1", "#", LineKind.Heading1),
            Line("標題 2", "##", LineKind.Heading2),
            Line("標題 3", "###", LineKind.Heading3),
            Line("一般文字", "", LineKind.Text),
            new ToolStripSeparator(),
            Inline("粗體", "**  Ctrl+B", "**"),
            Inline("斜體", "*  Ctrl+I", "*"),
            Inline("刪除線", "~~", "~~"),
            new ToolStripSeparator(),
            Line("項目清單", "-", LineKind.Bullet),
            Line("編號清單", "1.", LineKind.Numbered),
            Line("待辦事項", "- [ ]", LineKind.Task),
        });
        return (format, lineItems);
    }

    // ---------- 訊息處理 ----------

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_IME_STARTCOMPOSITION:
                _composing = true;
                break;

            case WM_IME_ENDCOMPOSITION:
                _composing = false;
                base.WndProc(ref m);
                if (_dirtyWhileComposing)
                {
                    _dirtyWhileComposing = false;
                    BeginInvoke(FormatChanged); // 等輸入法把結果文字放進來後再套用
                }
                return;

            case WM_LBUTTONDOWN:
            case WM_LBUTTONDBLCLK:
                int lp = unchecked((int)(long)m.LParam);
                if (TryToggleCheckbox(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF))))
                    return; // 不交給 RichEdit：點勾選框不移動游標、不開始選取
                break;

            case WM_SETCURSOR:
                if (CheckboxAt(PointToClient(Cursor.Position)) != null)
                {
                    Cursor.Current = Cursors.Hand;
                    m.Result = (IntPtr)1;
                    return;
                }
                break;
        }
        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            ClearFontCache();
            ContextMenuStrip?.Dispose();
        }
    }

    // ---------- Win32 / TOM ----------

    private const int WM_SETREDRAW = 0x000B;
    private const int WM_SETCURSOR = 0x0020;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_IME_STARTCOMPOSITION = 0x010D;
    private const int WM_IME_ENDCOMPOSITION = 0x010E;
    private const int WM_USER = 0x0400;
    private const int EM_GETOLEINTERFACE = WM_USER + 60;
    private const int EM_GETSCROLLPOS = WM_USER + 221;
    private const int EM_SETSCROLLPOS = WM_USER + 222;
    private const int GCS_COMPSTR = 0x0008;
    private const int TomSuspend = -9999995;
    private const int TomResume = -9999994;

    private const int EM_SETCHARFORMAT = WM_USER + 68;
    private const int SCF_SELECTION = 0x0001;
    private const uint CFM_HIDDEN = 0x0100;
    private const uint CFE_HIDDEN = 0x0100;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    /// <summary>richedit.h 的 CHARFORMAT2W（只用到 cbSize、dwMask、dwEffects）</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CHARFORMAT2
    {
        public int cbSize;
        public uint dwMask;
        public uint dwEffects;
        public int yHeight;
        public int yOffset;
        public int crTextColor;
        public byte bCharSet;
        public byte bPitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szFaceName;
        public short wWeight;
        public short sSpacing;
        public int crBackColor;
        public int lcid;
        public int dwReserved;
        public short sStyle;
        public short wKerning;
        public byte bUnderlineType;
        public byte bAnimation;
        public byte bRevAuthor;
        public byte bUnderlineColor;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref CHARFORMAT2 lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref POINT lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, out IntPtr lParam);

    [DllImport("imm32.dll")]
    private static extern IntPtr ImmGetContext(IntPtr hWnd);

    [DllImport("imm32.dll")]
    private static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

    [DllImport("imm32.dll")]
    private static extern int ImmGetCompositionStringW(IntPtr hIMC, int index, IntPtr buffer, int bufferLength);

    private POINT GetScrollPos()
    {
        var p = new POINT();
        SendMessage(Handle, EM_GETSCROLLPOS, IntPtr.Zero, ref p);
        return p;
    }

    private void SetScrollPos(POINT p) => SendMessage(Handle, EM_SETSCROLLPOS, IntPtr.Zero, ref p);

    /// <summary>取得 RichEdit 的 TOM 文件物件（用來暫停復原記錄）；拿不到時格式化仍可運作，只是會記入復原</summary>
    private ITextDocument? GetTextDocument()
    {
        try
        {
            SendMessage(Handle, EM_GETOLEINTERFACE, IntPtr.Zero, out IntPtr unknown);
            if (unknown == IntPtr.Zero)
                return null;
            try
            {
                return Marshal.GetObjectForIUnknown(unknown) as ITextDocument;
            }
            finally
            {
                Marshal.Release(unknown);
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// TOM ITextDocument（tom.h）。只會呼叫 Undo，但 COM 依 vtable 順序呼叫，
    /// 所以前面的方法都要照順序宣告（簽章不重要，不會被呼叫）。
    /// </summary>
    [ComImport, Guid("8CC497C0-A1DF-11CE-8098-00AA0047BE5D"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface ITextDocument
    {
        void GetName();
        void GetSelection();
        void GetStoryCount();
        void GetStoryRanges();
        void GetSaved();
        void SetSaved();
        void GetDefaultTabStop();
        void SetDefaultTabStop();
        void New();
        void Open();
        void Save();
        void Freeze();
        void Unfreeze();
        void BeginEditCollection();
        void EndEditCollection();
        [PreserveSig] int Undo(int count, out int done);
    }
}
