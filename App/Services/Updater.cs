using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using GaugeOverlay.Core.Data;

namespace GaugeOverlay.App.Services;

/// <summary>업데이트: asks GitHub for the latest release (<see cref="UpdateCheck"/>) and, only when the user asks, downloads,
/// verifies and installs it (<see cref="UpdateInstaller"/>). The app then restarts into the new exe.</summary>
public sealed class Updater : IDisposable
{
    static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(10), DownloadTimeout = TimeSpan.FromMinutes(10);

    /// <summary>Where downloads are unpacked: %LOCALAPPDATA%\GaugeOverlay\update.</summary>
    public static string UpdateDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GaugeOverlay", "update");

    /// <summary>The running version (the csproj's &lt;Version&gt;).</summary>
    public static Version Current { get; } = typeof(Updater).Assembly.GetName().Version ?? new Version(0, 0);

    readonly HttpClient _http = new() { Timeout = DownloadTimeout };

    public Updater() => _http.DefaultRequestHeaders.UserAgent.ParseAdd($"GaugeOverlay/{Current}");   // GitHub's API requires one

#if DEBUG
    // GAUGE_UPDATE_URL / GAUGE_UPDATE_PREFIX: a local test server in place of GitHub. Debug builds only.
    static string LatestUrl => Environment.GetEnvironmentVariable("GAUGE_UPDATE_URL") is { Length: > 0 } u ? u : UpdateCheck.LatestUrl;
    static string Prefix => Environment.GetEnvironmentVariable("GAUGE_UPDATE_PREFIX") is { Length: > 0 } p ? p : UpdateCheck.DownloadPrefix;
    static bool LocalTest => Prefix.StartsWith("http://localhost:", StringComparison.Ordinal);   // plain http allowed for it
#else
    static string LatestUrl => UpdateCheck.LatestUrl;
    static string Prefix => UpdateCheck.DownloadPrefix;
    const bool LocalTest = false;
#endif

    /// <summary>Whether the latest release could be read (false when offline, on a timeout, or a 404 while the repo is
    /// private), and that release if it's newer than this one.</summary>
    public async Task<(bool Checked, Release? Newer)> CheckAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(CheckTimeout);
            using var res = await _http.GetAsync(LatestUrl, cts.Token);
            if (!res.IsSuccessStatusCode) return (false, null);
            var release = UpdateCheck.Parse(await res.Content.ReadAsStringAsync(cts.Token), Prefix);
            return (true, release is not null && UpdateCheck.IsNewer(release.Version, Current) ? release : null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            Trace.WriteLine($"[update] check failed: {ex.Message}");
            return (false, null);
        }
    }

    /// <summary>Downloads <paramref name="release"/>'s zip and .sha256, refuses a zip whose hash differs, unpacks it and puts it
    /// in <paramref name="appDir"/>. Null on success, else why not (in Korean, for a tray notice); the old files stay then.</summary>
    public async Task<string?> InstallAsync(Release release, string appDir)
    {
        try
        {
            using var cts = new CancellationTokenSource(DownloadTimeout);
            Directory.CreateDirectory(UpdateDir);
            string zip = Path.Combine(UpdateDir, "update.zip"), newDir = Path.Combine(UpdateDir, "new");
            var shaText = new MemoryStream();
            await CopyCapped(release.ShaUrl, shaText, MaxSha, cts.Token);
            string sha = System.Text.Encoding.UTF8.GetString(shaText.ToArray());
            await using (var file = File.Create(zip))
                await CopyCapped(release.ZipUrl, file, MaxZip, cts.Token);
            // Hashing 80 MB and copying a 200 MB exe would freeze the window: off the UI thread.
            return await Task.Run(() =>
            {
                using (var file = File.OpenRead(zip))
                    if (!UpdateCheck.HashMatches(file, sha)) return "받은 업데이트 파일이 원본과 달라(SHA-256 불일치) 설치하지 않았습니다.";
                using (var archive = ZipFile.OpenRead(zip))
                    if (UpdateInstaller.ZipProblem([.. archive.Entries.Select(e => (e.FullName, e.Length))]) is { } bad) return bad;
                if (Directory.Exists(newDir)) Directory.Delete(newDir, recursive: true);
                ZipFile.ExtractToDirectory(zip, newDir);
                return UpdateInstaller.Swap(appDir, newDir) is { } why ? $"업데이트를 설치하지 못했습니다: {why}" : null;
            });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Trace.WriteLine($"[update] install failed: {ex}");
            return $"업데이트를 받지 못했습니다: {ex.Message}";
        }
    }

    const long MaxZip = 300_000_000;   // the zip is about 80 MB
    const int MaxSha = 1024;

    /// <summary>Copies a download into <paramref name="to"/>, refusing more than <paramref name="max"/> bytes and an answer
    /// that ended up off https (HttpClient follows GitHub's redirect to its file host).</summary>
    async Task CopyCapped(string url, Stream to, long max, CancellationToken ct)
    {
        using var res = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();
        if (res.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps && !LocalTest) throw new HttpRequestException("https가 아닌 주소로 옮겨졌습니다.");
        if (res.Content.Headers.ContentLength > max) throw new InvalidDataException("업데이트 파일이 너무 큽니다.");
        await using var from = await res.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[81920];
        long total = 0;
        for (int n; (n = await from.ReadAsync(buffer, ct)) > 0;)
        {
            if ((total += n) > max) throw new InvalidDataException("업데이트 파일이 너무 큽니다.");
            await to.WriteAsync(buffer.AsMemory(0, n), ct);
        }
    }

    public void Dispose() => _http.Dispose();
}
