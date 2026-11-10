using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Scr;

public sealed record UpdateInfo(Version Version, string ZipUrl, long Size, string? Sha256, string PageUrl);

/// <summary>
/// Checks GitHub for a newer Scr and installs it: download the release zip (only from this project's releases),
/// check its size and SHA-256, unpack it, then the new Scr.exe copies itself over the old files and starts again.
/// Nothing about you or your screenshots is sent; it's one plain request for the latest release.
/// </summary>
public static class Updater
{
    const string LatestApi = "https://api.github.com/repos/ecnot/scr/releases/latest";
    const string DownloadPrefix = "https://github.com/ecnot/scr/releases/download/";
    public const string ReleasesPage = "https://github.com/ecnot/scr/releases/latest";

    /// <summary>The running version (SCR_FAKE_VERSION pretends to be older, for testing an update).</summary>
    public static Version Current
    {
        get
        {
            if (Environment.GetEnvironmentVariable("SCR_FAKE_VERSION") is { Length: > 0 } fake && Version.TryParse(fake, out var f)) return f;
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    public static string CurrentText => $"{Current.Major}.{Current.Minor}.{Current.Build}";

    /// <summary>Test mode only (SCR_TEST=1): read a local release description instead of GitHub's.</summary>
    static string? TestFeed => Environment.GetEnvironmentVariable("SCR_TEST") == "1" ? Environment.GetEnvironmentVariable("SCR_TEST_FEED") : null;

    static HttpClient Client(TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Scr-updater/" + CurrentText); // GitHub asks for a User-Agent
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    /// <summary>The newer release, or null if this is the latest.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        JsonDocument doc;
        if (TestFeed is string feed)
        {
            await using var fs = File.OpenRead(feed);
            doc = await JsonDocument.ParseAsync(fs, cancellationToken: ct);
        }
        else
        {
            using var http = Client(TimeSpan.FromSeconds(20));
            using var resp = await http.GetAsync(LatestApi, ct);
            resp.EnsureSuccessStatusCode();
            await using var body = await resp.Content.ReadAsStreamAsync(ct);
            doc = await JsonDocument.ParseAsync(body, cancellationToken: ct);
        }
        using var _ = doc;
        var root = doc.RootElement;
        if (root.TryGetProperty("draft", out var d) && d.GetBoolean()) return null;
        if (root.TryGetProperty("prerelease", out var p) && p.GetBoolean()) return null;

        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest)) return null;
        latest = new Version(latest.Major, latest.Minor, Math.Max(0, latest.Build));
        if (latest <= Current) return null;

        string page = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? ReleasesPage : ReleasesPage;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            var url = asset.GetProperty("browser_download_url").GetString() ?? "";
            if (!name.StartsWith("Scr-", StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
            if (!url.StartsWith(TestFeed != null ? "file:" : DownloadPrefix, StringComparison.OrdinalIgnoreCase)) continue; // only this project's own releases
            long size = asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
            string? sha = null;
            if (asset.TryGetProperty("digest", out var dg) && dg.GetString() is string digest && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                sha = digest[7..];
            return new UpdateInfo(latest, url, size, sha, page);
        }
        return null; // a newer release without a zip: nothing to install
    }

    static string WorkFolder => Path.Combine(Path.GetTempPath(), "Scr-update");

    /// <summary>Downloads and unpacks the update, then starts the new Scr to finish. The caller then quits.</summary>
    public static async Task DownloadAndStartAsync(UpdateInfo u, CancellationToken ct = default)
    {
        string install = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        // Fail early if Scr's own folder can't be written to (for example under Program Files).
        string probe = Path.Combine(install, ".scr-update-test");
        try { File.WriteAllText(probe, "ok"); File.Delete(probe); }
        catch { throw new InvalidOperationException("Scr can't change its own folder. Move it somewhere you can write to (like C:\\Apps\\Scr) or update by hand."); }

        if (Directory.Exists(WorkFolder)) Directory.Delete(WorkFolder, true);
        Directory.CreateDirectory(WorkFolder);
        string zip = Path.Combine(WorkFolder, "update.zip");

        if (TestFeed != null && u.ZipUrl.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(new Uri(u.ZipUrl).LocalPath, zip);
        }
        else
        using (var http = Client(TimeSpan.FromMinutes(5)))
        using (var resp = await http.GetAsync(u.ZipUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            await using var from = await resp.Content.ReadAsStreamAsync(ct);
            await using var to = File.Create(zip);
            await from.CopyToAsync(to, ct);
        }

        var info = new FileInfo(zip);
        if (u.Size > 0 && info.Length != u.Size) throw new InvalidDataException("The download was incomplete. Try again later.");
        if (u.Sha256 != null)
        {
            await using var fs = File.OpenRead(zip);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct));
            if (!hash.Equals(u.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The download didn't match GitHub's checksum, so Scr didn't install it.");
        }

        string unpacked = Path.Combine(WorkFolder, "new");
        ZipFile.ExtractToDirectory(zip, unpacked);
        var exe = Directory.GetFiles(unpacked, "Scr.exe", SearchOption.AllDirectories).FirstOrDefault()
                  ?? throw new InvalidDataException("The update doesn't contain Scr.exe.");
        if (!File.Exists(Path.Combine(Path.GetDirectoryName(exe)!, "Scr.dll"))) throw new InvalidDataException("The update is missing files.");

        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
        psi.ArgumentList.Add("--finish-update");
        psi.ArgumentList.Add(install);
        psi.ArgumentList.Add(Environment.ProcessId.ToString());
        Process.Start(psi);
    }

    /// <summary>
    /// Runs in the new version, from the temporary folder: waits for the old Scr to quit, copies the new files over it,
    /// and starts it again in the tray. If anything goes wrong, the old Scr is started again as it was.
    /// </summary>
    public static void FinishUpdate(string install, int oldPid)
    {
        string log = Path.Combine(WorkFolder, "update-log.txt");
        try
        {
            try { using var p = Process.GetProcessById(oldPid); p.WaitForExit(20000); } catch { /* already gone */ }
            string from = AppContext.BaseDirectory;
            foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(install, Path.GetRelativePath(from, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                for (int i = 0; ; i++)
                {
                    try { File.Copy(file, target, true); break; }
                    catch (IOException) when (i < 40) { Thread.Sleep(250); } // a file can stay locked for a moment
                }
            }
            File.WriteAllText(log, $"Updated {install} to {CurrentText} at {DateTime.Now}\n");
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(log, ex.ToString()); } catch { }
        }
        var start = new ProcessStartInfo(Path.Combine(install, "Scr.exe")) { UseShellExecute = false, WorkingDirectory = install };
        start.ArgumentList.Add("--tray");
        start.ArgumentList.Add("--updated");
        try { Process.Start(start); } catch { }
    }
}
