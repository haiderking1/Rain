using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Rain;

/// <summary>Downloads audio (with square cover art and tags) via yt-dlp + ffmpeg.</summary>
public static partial class Downloader
{
    const string ProgressMarker = "RAINPROG ";
    const string FileMarker = "RAINFILE ";

    // Crops the thumbnail to a centered square so it looks like album art.
    const string SquareCover =
        "ThumbnailsConvertor+ffmpeg_o:-c:v mjpeg -qmin 1 -qscale:v 1 -vf crop=\"'if(gt(ih,iw),iw,ih)':'if(gt(iw,ih),ih,iw)'\"";

    [GeneratedRegex(@"\x1B\[[0-9;]*m")]
    private static partial Regex AnsiCodes();

    public static string? FindYtDlp() => FindOnPath("yt-dlp.exe");

    /// <summary>
    /// Downloads <paramref name="url"/> as m4a into <paramref name="folder"/> and returns the file path.
    /// Progress reports 0-100 while downloading, then -1 while converting.
    /// </summary>
    public static async Task<string> DownloadAsync(string url, string folder, IProgress<double> progress, CancellationToken ct)
    {
        var exe = FindYtDlp() ?? throw new InvalidOperationException("yt-dlp isn't installed (scoop install yt-dlp ffmpeg).");

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.Environment["PATH"] = FreshPath();
        psi.Environment["PYTHONIOENCODING"] = "utf-8";

        foreach (var arg in new[]
        {
            "--no-playlist",
            // Keep YouTube's native AAC (m4a) when available: no re-encode, faster and lossless vs. the source.
            "-f", "bestaudio[ext=m4a]/bestaudio", "-x", "--audio-format", "m4a",
            "--embed-metadata",
            "--embed-thumbnail", "--convert-thumbnails", "jpg", "--ppa", SquareCover,
            "--newline", "--progress",
            "--progress-template", $"download:{ProgressMarker}%(progress._percent_str)s",
            "--print", $"after_move:{FileMarker}%(filepath)s",
            "--encoding", "utf-8",
            "-o", Path.Combine(folder, "%(title)s.%(ext)s"),
            "--", url,
        })
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Couldn't start yt-dlp.");

        string? filePath = null;
        var lastError = "";

        var readOut = Task.Run(async () =>
        {
            while (await process.StandardOutput.ReadLineAsync() is { } raw)
            {
                var line = AnsiCodes().Replace(raw, "").Trim();
                if (line.StartsWith(ProgressMarker, StringComparison.Ordinal))
                {
                    var text = line[ProgressMarker.Length..].Trim().TrimEnd('%');
                    if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
                        progress.Report(percent >= 100 ? -1 : percent);
                }
                else if (line.StartsWith(FileMarker, StringComparison.Ordinal))
                {
                    filePath = line[FileMarker.Length..].Trim();
                }
            }
        }, CancellationToken.None);

        var readErr = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync() is { } line)
            {
                if (line.StartsWith("ERROR:", StringComparison.Ordinal)) lastError = line["ERROR:".Length..].Trim();
            }
        }, CancellationToken.None);

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }
        await Task.WhenAll(readOut, readErr);

        if (process.ExitCode != 0 || filePath is null || !File.Exists(filePath))
            throw new InvalidOperationException(lastError.Length > 0 ? lastError : $"yt-dlp failed (exit code {process.ExitCode}).");

        return filePath;
    }

    /// <summary>PATH as it is now in the registry, so tools installed after Rain started are found.</summary>
    static string FreshPath() => string.Join(';', new[]
    {
        Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine),
        Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User),
        Environment.GetEnvironmentVariable("PATH"),
    }.Where(p => !string.IsNullOrEmpty(p)));

    static string? FindOnPath(string exe)
    {
        foreach (var dir in FreshPath().Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
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
}
