using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Rain;

/// <summary>
/// The external tools downloads need (yt-dlp, ffmpeg, deno). Uses copies on PATH when present,
/// otherwise installs verified copies into Rain's own tools folder on request.
/// </summary>
public static partial class Tools
{
    public sealed record Tool(string Name, int SizeMb, string[] Executables);

    public static readonly Tool YtDlp = new("yt-dlp", 17, ["yt-dlp.exe"]);
    public static readonly Tool Ffmpeg = new("ffmpeg", 84, ["ffmpeg.exe", "ffprobe.exe"]);
    public static readonly Tool Deno = new("deno", 40, ["deno.exe"]);
    static readonly Tool[] All = [YtDlp, Ffmpeg, Deno];

    /// <summary>Rain's private tools folder (removed with the app on uninstall).</summary>
    public static string Dir { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Rain", "tools");

    /// <summary>Set RAIN_IGNORE_SYSTEM_TOOLS=1 to test the first-run flow on a machine that has the tools.</summary>
    static bool IgnoreSystemTools => Environment.GetEnvironmentVariable("RAIN_IGNORE_SYSTEM_TOOLS") == "1";

    /// <summary>PATH for child processes: Rain's tools first, then the current system PATH from the registry.</summary>
    public static string SearchPath => string.Join(';', new[]
    {
        Dir,
        IgnoreSystemTools ? null : Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine),
        IgnoreSystemTools ? null : Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User),
        IgnoreSystemTools ? null : Environment.GetEnvironmentVariable("PATH"),
    }.Where(p => !string.IsNullOrEmpty(p)));

    public static string? Find(string exe)
    {
        foreach (var dir in SearchPath.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(Environment.ExpandEnvironmentVariables(dir), exe);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException) { }
        }
        return null;
    }

    public static List<Tool> Missing() => All.Where(t => t.Executables.Any(e => Find(e) is null)).ToList();

    /// <summary>Downloads, verifies and unpacks every missing tool. Reports (tool name, percent).</summary>
    public static async Task InstallMissingAsync(IProgress<(string Tool, double Percent)> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Dir);
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Rain-music-player");

        foreach (var tool in Missing())
        {
            var report = (double p) => progress.Report((tool.Name, p));
            if (tool == YtDlp)
            {
                await InstallAsync(http, report, ct,
                    url: "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe",
                    sumsUrl: "https://github.com/yt-dlp/yt-dlp/releases/latest/download/SHA2-256SUMS",
                    fileName: "yt-dlp.exe");
            }
            else if (tool == Ffmpeg)
            {
                // Shared build: smaller download; the DLLs from bin/ go next to the exes.
                await InstallAsync(http, report, ct,
                    url: "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl-shared.zip",
                    sumsUrl: "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/checksums.sha256",
                    fileName: "ffmpeg-master-latest-win64-gpl-shared.zip",
                    extract: entry => entry.FullName.Contains("/bin/", StringComparison.Ordinal) && entry.Name.Length > 0 && entry.Name != "ffplay.exe");
            }
            else if (tool == Deno)
            {
                await InstallAsync(http, report, ct,
                    url: "https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip",
                    sumsUrl: "https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip.sha256sum",
                    fileName: "deno-x86_64-pc-windows-msvc.zip",
                    extract: entry => entry.Name == "deno.exe");
            }
        }
    }

    static async Task InstallAsync(HttpClient http, Action<double> report, CancellationToken ct,
        string url, string sumsUrl, string fileName, Func<ZipArchiveEntry, bool>? extract = null)
    {
        var temp = Path.Combine(Dir, fileName + ".part");
        try
        {
            await DownloadAsync(http, url, temp, report, ct);
            await VerifyAsync(http, sumsUrl, fileName, temp, ct);

            if (extract is null)
            {
                File.Move(temp, Path.Combine(Dir, fileName), overwrite: true);
                return;
            }

            using (var zip = ZipFile.OpenRead(temp))
            {
                foreach (var entry in zip.Entries.Where(extract))
                    entry.ExtractToFile(Path.Combine(Dir, entry.Name), overwrite: true);
            }
        }
        finally
        {
            File.Delete(temp);
        }
    }

    static async Task DownloadAsync(HttpClient http, string url, string path, Action<double> report, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? 0;

        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var target = File.Create(path);
        var buffer = new byte[128 * 1024];
        long done = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), ct);
            done += read;
            if (total > 0) report(done * 100.0 / total);
        }
    }

    [GeneratedRegex("[0-9a-fA-F]{64}")]
    private static partial Regex Sha256Hex();

    /// <summary>Checks the file against the publisher's SHA-256 list (or single-hash file).</summary>
    static async Task VerifyAsync(HttpClient http, string sumsUrl, string fileName, string path, CancellationToken ct)
    {
        // Either "hash  name" lines (yt-dlp, ffmpeg) or a file holding a single hash (deno's Get-FileHash output).
        var sums = await http.GetStringAsync(sumsUrl, ct);
        var expected = sums.Split('\n')
            .Where(l => l.TrimEnd().EndsWith(fileName, StringComparison.Ordinal))
            .Select(l => Sha256Hex().Match(l).Value)
            .FirstOrDefault(h => h.Length > 0);
        if (expected is null)
        {
            var hashes = Sha256Hex().Matches(sums);
            if (hashes.Count != 1)
                throw new InvalidOperationException($"No checksum published for {fileName}.");
            expected = hashes[0].Value;
        }

        string actual;
        await using (var file = File.OpenRead(path))
            actual = Convert.ToHexString(await SHA256.HashDataAsync(file, ct));

        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{fileName} failed its checksum check, not installing it.");
    }
}
