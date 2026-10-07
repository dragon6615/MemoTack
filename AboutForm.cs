using System.Diagnostics;

namespace MemoTack;

/// <summary>
/// 關於視窗：版本、作者、授權，以及專案頁、更新紀錄、問題回報的連結。
/// </summary>
public class AboutForm : StyledDialog
{
    public AboutForm() : base("關於 MemoTack")
    {
        StartPosition = FormStartPosition.CenterScreen;

        var body = MakeBody();
        AddRow(body, "版本", MakeText(UpdateChecker.CurrentVersion.ToString(3)));
        AddRow(body, "作者", MakeText("Dragon"));
        AddRow(body, "授權", MakeText("MIT License（免費、開放原始碼）"));
        AddRow(body, "專案網站", MakeLink("GitHub", UpdateChecker.RepoUrl));
        AddRow(body, "更新紀錄", MakeLink("查看所有版本的更新內容", UpdateChecker.RepoUrl + "/releases"));
        AddRow(body, "問題回報", MakeLink("回報問題或提出建議", UpdateChecker.RepoUrl + "/issues"));

        var btnOk = MakeButton("確定", primary: true);
        btnOk.DialogResult = DialogResult.OK;

        // 標題用單色符號：📌 這類只有彩色字形的 emoji，在一般 Label 上會畫成方框
        SetLayout(MakeHeader("ℹ  MemoTack", "仿 Windows Sticky Notes 的桌面便箋小工具", NoteForm.Palette[0].Header),
                  body, MakeFooter(null, btnOk));

        AcceptButton = btnOk;
        CancelButton = btnOk;
    }

    private Label MakeText(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = TextStrong,
        Margin = new Padding(0, Dpi(4), 0, Dpi(4)),
    };

    private LinkLabel MakeLink(string text, string url)
    {
        var link = new LinkLabel
        {
            Text = text,
            AutoSize = true,
            LinkColor = Primary,
            ActiveLinkColor = Primary,
            LinkBehavior = LinkBehavior.HoverUnderline,
            Margin = new Padding(0, Dpi(4), 0, Dpi(4)),
        };
        link.LinkClicked += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
                // 沒有預設瀏覽器等狀況：不讓程式崩潰
            }
        };
        return link;
    }
}
