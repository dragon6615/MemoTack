namespace MemoTack;

/// <summary>
/// 發現新版時的詢問視窗：顯示更新內容，三個選擇——立即更新、稍後提醒、略過這個版本。
/// 「立即更新」會在視窗內下載並比對檢查碼，成功後以 DialogResult.OK 結束，安裝檔路徑放在 InstallerPath。
/// </summary>
public class UpdateForm : StyledDialog
{
    public enum Choice { Later, Update, Skip }

    private readonly UpdateInfo _info;
    private readonly Label _status;
    private readonly Button _btnUpdate;
    private readonly Button _btnLater;
    private readonly LinkLabel _skip;
    private CancellationTokenSource? _download;

    public Choice Result { get; private set; } = Choice.Later;

    /// <summary>下載並驗證完成的安裝檔（Result 為 Update 時才有）</summary>
    public string? InstallerPath { get; private set; }

    public UpdateForm(UpdateInfo info) : base("MemoTack 更新")
    {
        _info = info;
        StartPosition = FormStartPosition.CenterScreen;

        // ---- 更新內容：用便箋的 markdown 編輯器唯讀顯示，標題、清單直接排好版 ----
        var notes = new MarkdownTextBox
        {
            ReadOnly = true,
            BackColor = Surface,
            ForeColor = TextStrong,
            Font = OwnFont(10f),
            ScrollBars = RichTextBoxScrollBars.Vertical,
            Size = new Size(Dpi(520), Dpi(280)),
            TabStop = false,
            Margin = new Padding(0, 0, 0, Dpi(8)),
            Markdown = info.Notes.Length > 0 ? info.Notes : "（這個版本沒有更新說明）",
        };
        notes.ContextMenuStrip = null; // 唯讀內容不需要編輯選單

        _status = new Label
        {
            Text = "更新時 MemoTack 會先存檔並關閉，安裝完成後自動重新開啟。",
            AutoSize = true,
            ForeColor = TextMuted,
            Font = OwnFont(9f),
            Margin = new Padding(0, 0, 0, Dpi(4)),
        };

        var body = MakeBody();
        body.Controls.Add(notes, 0, 0);
        body.SetColumnSpan(notes, 2);
        body.Controls.Add(_status, 0, 1);
        body.SetColumnSpan(_status, 2);

        // ---- 底部：左側略過這個版本，右側立即更新 / 稍後提醒 ----
        _btnUpdate = MakeButton("立即更新", primary: true);
        _btnUpdate.Click += async (_, _) => await DownloadAndCloseAsync();
        _btnLater = MakeButton("稍後提醒", primary: false);
        _btnLater.DialogResult = DialogResult.Cancel;
        _skip = new LinkLabel
        {
            Text = "略過這個版本",
            AutoSize = true,
            LinkColor = TextMuted,
            ActiveLinkColor = TextMuted,
            LinkBehavior = LinkBehavior.HoverUnderline,
            Margin = new Padding(Dpi(4), 0, Dpi(16), 0),
        };
        _skip.LinkClicked += (_, _) =>
        {
            Result = Choice.Skip;
            DialogResult = DialogResult.Cancel;
        };

        // 標題用單色符號：🎉 這類只有彩色字形的 emoji，在一般 Label 上會畫成方框
        SetLayout(MakeHeader("⬆  有新版本可以更新",
                             $"MemoTack {info.Version.ToString(3)}（目前是 {UpdateChecker.CurrentVersion.ToString(3)}）",
                             NoteForm.Palette[0].Header),
                  body, MakeFooter(_skip, _btnUpdate, _btnLater));

        AcceptButton = _btnUpdate;
        CancelButton = _btnLater;
    }

    private async Task DownloadAndCloseAsync()
    {
        _btnUpdate.Enabled = _skip.Enabled = false;
        _status.ForeColor = TextMuted;
        _status.Text = "下載中…";
        _download = new CancellationTokenSource();
        try
        {
            var progress = new Progress<int>(p => _status.Text = $"下載中… {p}%");
            InstallerPath = await UpdateChecker.DownloadAsync(_info, progress, _download.Token);
            Result = Choice.Update;
            DialogResult = DialogResult.OK;
        }
        catch (OperationCanceledException)
        {
            // 下載中按了「稍後提醒」關閉視窗
        }
        catch (Exception ex)
        {
            _status.ForeColor = Danger;
            _status.Text = "下載失敗：" + ex.Message;
            _btnUpdate.Enabled = _skip.Enabled = true;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult != DialogResult.OK)
            _download?.Cancel(); // 下載到一半關掉視窗：停止下載
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _download?.Dispose();
        base.Dispose(disposing);
    }
}
