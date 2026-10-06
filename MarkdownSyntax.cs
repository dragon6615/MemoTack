using System.Text;
using System.Text.RegularExpressions;

namespace MemoTack;

/// <summary>一個字元的 markdown 樣式（可組合）</summary>
[Flags]
public enum MdStyle
{
    None = 0,
    Marker = 1,    // 語法符號（**、#、- 等）：變淡但保留
    Bold = 2,
    Italic = 4,
    Strike = 8,
    Checkbox = 16, // 待辦勾選框（[ ] 或 ☐／☑）：可點擊
    Done = 32,     // 已完成待辦的內容：變灰 + 刪除線
    ListMark = 64, // 清單記號（• 或 1.）：屬於內容的一部分，一律顯示（不像 Marker 會被隱藏）
}

/// <summary>單行的解析結果</summary>
public sealed class MdLine
{
    /// <summary>標題層級 1–3；0 = 不是標題</summary>
    public int HeadingLevel { get; init; }

    /// <summary>待辦勾選框在行內的位置（「[」或 ☐／☑）；-1 = 不是待辦</summary>
    public int CheckboxStart { get; init; } = -1;

    /// <summary>勾選框長度：markdown 寫法「[ ]」為 3，編輯器顯示的 ☐／☑ 為 1</summary>
    public int CheckboxLength { get; init; }

    public bool TaskDone { get; init; }

    /// <summary>每個字元的樣式，長度等於該行字數</summary>
    public required MdStyle[] Styles { get; init; }

    /// <summary>把相同樣式的連續字元合併成區段</summary>
    public IEnumerable<(int Start, int Length, MdStyle Style)> Runs()
    {
        int start = 0;
        for (int i = 1; i <= Styles.Length; i++)
        {
            if (i == Styles.Length || Styles[i] != Styles[start])
            {
                yield return (start, i - start, Styles[start]);
                start = i;
            }
        }
    }
}

/// <summary>清單延續資訊：按 Enter 時下一行要加的前綴</summary>
public readonly record struct ListContinuation(int PrefixLength, string NextPrefix, bool IsEmptyItem);

/// <summary>整行的格式（右鍵選單「Markdown 格式」用）；標題的數值即層級</summary>
public enum LineKind { Text = 0, Heading1 = 1, Heading2 = 2, Heading3 = 3, Bullet, Numbered, Task }

/// <summary>
/// 便箋用的 markdown 子集解析（純邏輯，不碰 UI）：
/// 標題 #～###、粗體 **、斜體 *、刪除線 ~~、清單 - * + 1.、待辦 - [ ] / - [x]。
/// 以「行」為單位解析，不支援跨行語法——便箋內容短，逐行處理才能在打字時只重畫目前這一行。
///
/// 待辦與清單有兩種寫法：存檔用標準 markdown「- [ ]」「- [x]」「- 」（與其他工具相容），
/// 編輯器畫面上換成 ☐／☑／• 顯示（ToEditor / ToStorage 互轉），兩種寫法這裡都認得。
/// 其餘語法依標準 markdown：標題的 # 後面要有空白（「#標籤」不是標題）。
/// </summary>
public static class MarkdownSyntax
{
    public const char Unchecked = '☐';
    public const char Checked = '☑';

    private static readonly Regex HeadingRx = new(@"^(#{1,3})[ \t]+", RegexOptions.Compiled);
    private static readonly Regex TaskRx = new(@"^([ \t]*)([-*+])[ \t]+\[([ xX])\](?=[ \t]|$)", RegexOptions.Compiled);
    private static readonly Regex GlyphTaskRx = new(@"^([ \t]*)([☐☑])(?=[ \t]|$)", RegexOptions.Compiled);
    public const char Bullet = '•';

    private static readonly Regex BulletRx = new(@"^([ \t]*)([-*+•])[ \t]+", RegexOptions.Compiled);
    private static readonly Regex OrderedRx = new(@"^([ \t]*)(\d{1,9})([.)])[ \t]+", RegexOptions.Compiled);

    // 打字時自動轉換（後面要接空白才轉，避免打到一半就變）：
    // 待辦「- [ ] 」「- [x] 」「[] 」等 → ☐／☑；清單「- 」「* 」「+ 」→ •（「• [ ] 」也算待辦：打「- 」會先變 •）
    private static readonly Regex TypedTaskRx = new(@"^([ \t]*)(?:[-*+•][ \t]+)?\[([ xX]?)\][ \t]", RegexOptions.Compiled);
    private static readonly Regex TypedBulletRx = new(@"^([ \t]*)[-*+][ \t]", RegexOptions.Compiled);

    // 整份文字互轉用（多行模式）
    private static readonly Regex TaskLineRx = new(@"^([ \t]*)[-*+][ \t]+\[([ xX])\](?=[ \t]|$)", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex GlyphLineRx = new(@"^([ \t]*)([☐☑])(?=[ \t]|$)", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex BulletLineRx = new(@"^([ \t]*)[-*+][ \t]+", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex GlyphBulletLineRx = new(@"^([ \t]*)•[ \t]+", RegexOptions.Compiled | RegexOptions.Multiline);

    // 行內語法：內容前後不能是空白（「a * b * c」不算斜體）
    private static readonly Regex BoldRx = new(@"\*\*(?=\S)(.+?)(?<=\S)\*\*", RegexOptions.Compiled);
    private static readonly Regex StrikeRx = new(@"~~(?=\S)(.+?)(?<=\S)~~", RegexOptions.Compiled);
    private static readonly Regex ItalicRx = new(@"(?<!\*)\*(?=[^\s*])(.+?)(?<=[^\s*])\*(?!\*)", RegexOptions.Compiled);

    public static MdLine Analyze(string line)
    {
        var styles = new MdStyle[line.Length];
        int heading = 0, checkbox = -1, checkboxLength = 0, body = 0;
        bool done = false;
        Match m;

        if ((m = HeadingRx.Match(line)).Success)
        {
            heading = m.Groups[1].Length;
            Mark(styles, 0, m.Length, MdStyle.Marker);
            body = m.Length;
        }
        else if ((m = TaskRx.Match(line)).Success)
        {
            int bullet = m.Groups[2].Index;
            checkbox = m.Groups[3].Index - 1; // 「[」
            checkboxLength = 3;
            Mark(styles, bullet, checkbox - bullet, MdStyle.Marker);
            done = m.Groups[3].Value is "x" or "X";
            body = m.Length;
        }
        else if ((m = GlyphTaskRx.Match(line)).Success)
        {
            checkbox = m.Groups[2].Index;
            checkboxLength = 1;
            done = m.Groups[2].Value[0] == Checked;
            body = m.Length;
        }
        else if ((m = BulletRx.Match(line)).Success || (m = OrderedRx.Match(line)).Success)
        {
            int symbol = m.Groups[2].Index;
            Mark(styles, symbol, m.Length - symbol, MdStyle.ListMark);
            body = m.Length;
        }

        if (checkbox >= 0)
        {
            Mark(styles, checkbox, checkboxLength, MdStyle.Checkbox);
            if (done)
            {
                // 刪除線從第一個字開始畫，不含勾選框後面的空白
                int text = body;
                while (text < line.Length && line[text] is ' ' or '\t') text++;
                Mark(styles, text, line.Length - text, MdStyle.Done);
            }
        }

        // 粗體先處理：它的 ** 標成符號後，斜體就不會把其中一個 * 誤認成自己的
        Inline(line, body, styles, BoldRx, 2, MdStyle.Bold);
        Inline(line, body, styles, StrikeRx, 2, MdStyle.Strike);
        Inline(line, body, styles, ItalicRx, 1, MdStyle.Italic);

        return new MdLine
        {
            HeadingLevel = heading,
            CheckboxStart = checkbox,
            CheckboxLength = checkboxLength,
            TaskDone = done,
            Styles = styles,
        };
    }

    private static void Inline(string line, int from, MdStyle[] styles, Regex rx, int markerLength, MdStyle style)
    {
        for (var m = rx.Match(line, from); m.Success; m = m.NextMatch())
        {
            int end = m.Index + m.Length - 1;
            if (styles[m.Index].HasFlag(MdStyle.Marker) || styles[end].HasFlag(MdStyle.Marker))
                continue; // 符號已被其他語法用掉
            var content = m.Groups[1];
            Mark(styles, m.Index, markerLength, MdStyle.Marker);
            Mark(styles, end - markerLength + 1, markerLength, MdStyle.Marker);
            Mark(styles, content.Index, content.Length, style);
        }
    }

    private static void Mark(MdStyle[] styles, int start, int length, MdStyle style)
    {
        for (int i = start; i < start + length && i < styles.Length; i++)
            styles[i] |= style;
    }

    /// <summary>
    /// 清單或待辦行按 Enter 時要延續的前綴；不是清單行回傳 null。
    /// 待辦延續為未勾選（☐ 或「- [ ] 」，跟著目前這行的寫法），編號清單自動 +1。
    /// </summary>
    public static ListContinuation? Continue(string line)
    {
        Match m;
        if ((m = GlyphTaskRx.Match(line)).Success)
        {
            int prefix = Math.Min(line.Length, m.Length + 1); // 含 ☐ 後的空白
            return new ListContinuation(prefix, $"{m.Groups[1].Value}{Unchecked} ", line[prefix..].Trim().Length == 0);
        }
        if ((m = TaskRx.Match(line)).Success)
        {
            int prefix = Math.Min(line.Length, m.Length + 1); // 含「]」後的空白
            return new ListContinuation(prefix, $"{m.Groups[1].Value}{m.Groups[2].Value} [ ] ", line[prefix..].Trim().Length == 0);
        }
        if ((m = OrderedRx.Match(line)).Success && long.TryParse(m.Groups[2].Value, out long n))
            return new ListContinuation(m.Length, $"{m.Groups[1].Value}{n + 1}{m.Groups[3].Value} ", line[m.Length..].Trim().Length == 0);
        if ((m = BulletRx.Match(line)).Success)
            return new ListContinuation(m.Length, $"{m.Groups[1].Value}{m.Groups[2].Value} ", line[m.Length..].Trim().Length == 0);
        return null;
    }

    /// <summary>
    /// 這一行目前的整行格式，以及行首前綴（含縮排與其後空白）的長度、縮排。
    /// 換格式時：保留縮排、去掉舊前綴、加上新前綴。
    /// </summary>
    public static (LineKind Kind, int PrefixLength, string Indent) LinePrefix(string line)
    {
        Match m;
        if ((m = HeadingRx.Match(line)).Success)
            return ((LineKind)m.Groups[1].Length, m.Length, string.Empty);
        if ((m = TaskRx.Match(line)).Success || (m = GlyphTaskRx.Match(line)).Success)
            return (LineKind.Task, SkipSpaces(line, m.Length), m.Groups[1].Value);
        if ((m = OrderedRx.Match(line)).Success)
            return (LineKind.Numbered, m.Length, m.Groups[1].Value);
        if ((m = BulletRx.Match(line)).Success)
            return (LineKind.Bullet, m.Length, m.Groups[1].Value);
        int indent = SkipSpaces(line, 0);
        return (LineKind.Text, indent, line[..indent]);
    }

    private static int SkipSpaces(string line, int i)
    {
        while (i < line.Length && line[i] is ' ' or '\t') i++;
        return i;
    }

    /// <summary>存檔內容 → 編輯器顯示：行首的「- [ ]」「- [x]」換成 ☐／☑，其餘清單的「- 」「* 」「+ 」換成「• 」</summary>
    public static string ToEditor(string markdown)
    {
        // 先轉待辦，剩下的「- 」才是一般清單
        string text = TaskLineRx.Replace(markdown, m => m.Groups[1].Value + (m.Groups[2].Value == " " ? Unchecked : Checked));
        return BulletLineRx.Replace(text, m => m.Groups[1].Value + Bullet + " ");
    }

    /// <summary>編輯器顯示 → 存檔內容：行首的 ☐／☑、• 換回標準 markdown「- [ ]」「- [x]」「- 」</summary>
    public static string ToStorage(string editorText)
    {
        string text = GlyphLineRx.Replace(editorText, m => m.Groups[1].Value + (m.Groups[2].Value[0] == Unchecked ? "- [ ]" : "- [x]"));
        return GlyphBulletLineRx.Replace(text, m => m.Groups[1].Value + "- ");
    }

    /// <summary>
    /// 打字時的自動轉換：行首剛打完待辦（「- [ ] 」「[] 」…）或清單（「- 」「* 」「+ 」）寫法時，
    /// 回傳要被取代的長度與 ☐／☑／• 前綴；否則 null。呼叫端要確認游標正好在這段寫法之後。
    /// </summary>
    public static (int Length, string Replacement)? TypedPrefix(string line)
    {
        var m = TypedTaskRx.Match(line);
        if (m.Success)
        {
            char glyph = m.Groups[2].Value is "x" or "X" ? Checked : Unchecked;
            return (m.Length, $"{m.Groups[1].Value}{glyph} ");
        }
        m = TypedBulletRx.Match(line);
        if (m.Success)
            return (m.Length, $"{m.Groups[1].Value}{Bullet} ");
        return null;
    }

    /// <summary>去掉 markdown 符號後的純文字（選單、通知的預覽用）：「- [ ] **寫週報**」→「寫週報」</summary>
    public static string PlainText(string line)
    {
        var md = Analyze(line);
        var sb = new StringBuilder(line.Length);
        for (int i = 0; i < line.Length; i++)
        {
            if ((md.Styles[i] & (MdStyle.Marker | MdStyle.Checkbox | MdStyle.ListMark)) == 0)
                sb.Append(line[i]);
        }
        return sb.ToString().Trim();
    }
}
