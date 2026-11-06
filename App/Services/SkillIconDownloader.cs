using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;
using GaugeOverlay.Core.Data;

namespace GaugeOverlay.App.Services;

/// <summary>A skill icon this PC never captured, fetched once from Smilegate's CDN when a timeline needs it (skill-icons.plan.md,
/// user 2026-11-05) and kept in %APPDATA%\GaugeOverlay\skillicons. One at a time; a failure (offline, a refused or non-PNG file)
/// is not retried this session and the cast keeps its name chip. Smilegate's CDN sees which icons are fetched (the IP and path).</summary>
public static class SkillIconDownloader
{
    public static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GaugeOverlay", "skillicons");
    const int MaxBytes = 64 * 1024;   // a 64×64 icon is ~10 KB
    const int MaxSide = 128;          // pixels: a small file may still declare a huge picture
    const int MaxAsked = 500;         // icons a session may request (an imported file can name many)
    // No redirects: only the fixed host's own answer is taken. No User-Agent or other identifying header is sent.
    static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(5), MaxResponseContentBufferSize = MaxBytes };
    static readonly SemaphoreSlim One = new(1, 1);
    static readonly ConcurrentDictionary<string, byte> Asked = new();   // requested or failed this session

    /// <summary>An icon's file was written (icon, path), on a pool thread: every page waiting for it may use it.</summary>
    public static event Action<string, string>? Landed;

    /// <summary>The icon's file when it is here; null when not (or not an icon).</summary>
    public static string? Local(string icon) =>
        SkillIconSource.IsValid(icon) && SkillIconSource.LocalPath(Root, icon) is var path && File.Exists(path) ? path : null;

    /// <summary>Fetches the icon once; <see cref="Landed"/> tells when its file is written.</summary>
    public static void Request(string icon)
    {
        if (!SkillIconSource.IsValid(icon) || Asked.Count >= MaxAsked || !Asked.TryAdd(icon, 0)) return;
        _ = Task.Run(async () =>
        {
            await One.WaitAsync();
            try
            {
                var bytes = await Http.GetByteArrayAsync(SkillIconSource.Url(icon));
                using (var ms = new MemoryStream(bytes))   // a small PNG, or it is refused: the header is read, not the pixels
                    if (new PngBitmapDecoder(ms, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0] is { PixelWidth: > MaxSide } or { PixelHeight: > MaxSide })
                        throw new InvalidOperationException("not an icon's size");
                var path = SkillIconSource.LocalPath(Root, icon);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllBytesAsync(path + ".tmp", bytes);
                File.Move(path + ".tmp", path, overwrite: true);
                Landed?.Invoke(icon, path);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException
                or NotSupportedException or FileFormatException or InvalidOperationException or ArgumentException)
            {
                Trace.WriteLine($"[skillicons] {icon}: {ex.Message}");
            }
            finally { One.Release(); }
        });
    }
}
