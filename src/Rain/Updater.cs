using Velopack;
using Velopack.Sources;

namespace Rain;

/// <summary>Checks GitHub Releases for a newer Rain and downloads it in the background (installed copies only).</summary>
public sealed class Updater
{
    const string RepoUrl = "https://github.com/haiderking1/Rain";

    readonly UpdateManager _manager = new(new GithubSource(RepoUrl, accessToken: null, prerelease: false));
    UpdateInfo? _ready;

    public string? ReadyVersion => _ready?.TargetFullRelease.Version.ToString();

    /// <summary>Returns true when an update has been downloaded and is ready to apply.</summary>
    public async Task<bool> CheckAndDownloadAsync()
    {
        if (!_manager.IsInstalled) return false; // dev builds and the portable zip don't self-update

        var info = await _manager.CheckForUpdatesAsync();
        if (info is null) return false;

        await _manager.DownloadUpdatesAsync(info);
        _ready = info;
        return true;
    }

    /// <summary>Applies the update and relaunches Rain. Exits this process.</summary>
    public void RestartNow()
    {
        if (_ready is not null) _manager.ApplyUpdatesAndRestart(_ready);
    }

    /// <summary>Applies the downloaded update quietly once Rain has closed.</summary>
    public void ApplyOnExit()
    {
        if (_ready is not null) _manager.WaitExitThenApplyUpdates(_ready, silent: true, restart: false);
    }
}
