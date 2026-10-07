using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MemoTack;

/// <summary>GitHub 上的新版本資訊</summary>
public sealed record UpdateInfo(Version Version, string Notes, string InstallerName, string InstallerUrl, string? Sha256);

/// <summary>
/// 自動更新：查 GitHub Releases 最新版 → 下載安裝檔 → 比對 SHA-256 → 執行安裝程式。
/// 依賴發佈流程的約定：安裝檔名為 MemoTack-Setup-x.y.z.exe，Release 說明附「雜湊值  檔名」一行。
/// 只用 .NET 內建的 HttpClient，維持零依賴。
/// </summary>
public static class UpdateChecker
{
    public const string RepoUrl = "https://github.com/dragon6615/MemoTack";

    /// <summary>最近的 Release 清單（新到舊）；跨多版更新時要把中間每一版的說明都列出來</summary>
    private const string ReleasesUrl = "https://api.github.com/repos/dragon6615/MemoTack/releases?per_page=30";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        // GitHub API 要求一定要有 User-Agent
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MemoTack", CurrentVersion.ToString()));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
    }

    /// <summary>目前執行中的版本（x.y.z）</summary>
    public static Version CurrentVersion
    {
        get
        {
            var v = typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    /// <summary>
    /// 查 GitHub 最新正式版（不含草稿與預先發行版）。比目前版本新就回傳資訊，否則 null。
    /// 更新內容包含目前版本之後的每一版（新到舊），跨版更新的人才不會漏看中間的改動。
    /// 連線失敗會丟出例外，由呼叫端決定要不要提示（自動檢查時安靜略過）。
    /// </summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken cancel = default)
    {
        using var response = await Http.GetAsync(ReleasesUrl, cancel);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));

        var newer = new List<(Version Version, JsonElement Release)>();
        foreach (var release in json.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean())
                continue;
            string tag = release.GetProperty("tag_name").GetString() ?? "";
            if (Version.TryParse(tag.TrimStart('v', 'V'), out var v) && v > CurrentVersion)
                newer.Add((v, release));
        }
        if (newer.Count == 0)
            return null;
        newer.Sort((a, b) => b.Version.CompareTo(a.Version));

        var (latest, root) = newer[0];
        string installerName = $"MemoTack-Setup-{latest.ToString(3)}.exe";
        string? url = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() == installerName)
                url = asset.GetProperty("browser_download_url").GetString();
        }
        if (url == null)
            return null; // 發佈還沒完成（安裝檔還沒上傳），下次再看

        string body = BodyOf(root);
        var sha = Regex.Match(body, @"\b([0-9A-Fa-f]{64})\s+" + Regex.Escape(installerName));

        // 只差一版：照舊不加版本標題（視窗標題已寫版本）；跨多版：每版前面加「# x.y.z」
        string notes = newer.Count == 1
            ? ReleaseNotes(body)
            : string.Join("\n\n", newer.Select(n => $"# {n.Version.ToString(3)}\n" + ReleaseNotes(BodyOf(n.Release))));
        return new UpdateInfo(latest, notes, installerName, url, sha.Success ? sha.Groups[1].Value : null);
    }

    private static string BodyOf(JsonElement release) =>
        release.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";

    /// <summary>
    /// Release 說明只取更新內容（「---」之後是安裝說明與檢查碼，給網頁看的），
    /// 標題各降一級：CHANGELOG 用 ### / ####，編輯器支援到 ###。
    /// </summary>
    private static string ReleaseNotes(string body)
    {
        int cut = body.IndexOf("\n---", StringComparison.Ordinal);
        string notes = (cut >= 0 ? body[..cut] : body).Replace("\r\n", "\n").Trim();
        return Regex.Replace(notes, @"^#(#{1,3}) ", "$1 ", RegexOptions.Multiline);
    }

    /// <summary>
    /// 下載安裝檔到暫存資料夾並比對 SHA-256；不一致就刪掉並丟出例外，不會執行來路不明的檔案。
    /// 程式自己下載的檔案沒有「來自網路」標記，執行時不會跳 SmartScreen 警告。
    /// </summary>
    public static async Task<string> DownloadAsync(UpdateInfo info, IProgress<int>? progress, CancellationToken cancel = default)
    {
        if (info.Sha256 == null)
            throw new InvalidOperationException("Release 說明裡找不到安裝檔的檢查碼");

        CleanupDownloads(); // 同一時間只留這次要裝的這一個
        Directory.CreateDirectory(DownloadDir);
        string path = Path.Combine(DownloadDir, info.InstallerName);

        using (var response = await Http.GetAsync(info.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, cancel))
        {
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? 0;
            await using var source = await response.Content.ReadAsStreamAsync(cancel);
            await using var target = File.Create(path);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancel)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancel);
                done += read;
                if (total > 0)
                    progress?.Report((int)(done * 100 / total));
            }
        }

        string actual;
        await using (var file = File.OpenRead(path))
            actual = Convert.ToHexString(await SHA256.HashDataAsync(file, cancel));
        if (!actual.Equals(info.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(path);
            throw new InvalidOperationException("下載的安裝檔檢查碼不符，已刪除，請稍後再試");
        }
        return path;
    }

    private static string DownloadDir => Path.Combine(Path.GetTempPath(), "MemoTack-Update");

    /// <summary>
    /// 刪掉下載過的安裝檔（每個約 60 MB，不清會一版一版累積在暫存資料夾）。
    /// 安裝當下檔案還被安裝程式占用刪不掉，所以在更新後重新開啟、安裝程式結束後才清。
    /// 刪不掉（仍被占用、權限）就留到下次，不影響程式。
    /// </summary>
    public static void CleanupDownloads()
    {
        try
        {
            if (Directory.Exists(DownloadDir))
                Directory.Delete(DownloadDir, recursive: true);
        }
        catch
        {
            // 下次再清
        }
    }

    /// <summary>
    /// 以靜默模式執行安裝程式（只顯示進度條）。沿用上次的安裝方式：裝給所有使用者的會跳系統管理員確認。
    /// 安裝完成後由安裝程式重新開啟 MemoTack（installer.iss 的 skipifnotsilent 項目）。
    /// </summary>
    public static void RunInstaller(string path) =>
        Process.Start(new ProcessStartInfo(path, "/SILENT /SUPPRESSMSGBOXES /NORESTART") { UseShellExecute = true });
}
