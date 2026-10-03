using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Rain;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    const string PlayGlyphText = "";
    const string PauseGlyphText = "";

    readonly AudioPlayer _player = new();
    readonly DispatcherTimer _timer;
    readonly Settings _settings = Settings.Load();
    readonly Stack<Track> _history = new();
    readonly Random _random = new();
    CancellationTokenSource? _scanCts;

    public MainViewModel()
    {
        _tracksView = CreateView(_tracks);
        _volume = Math.Clamp(_settings.Volume, 0, 1);
        _player.Volume = (float)_volume;
        _isShuffle = _settings.Shuffle;
        _repeat = _settings.Repeat;

        _player.TrackEnded += (_, _) => Application.Current.Dispatcher.InvokeAsync(() => Next(auto: true));
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Normal, (_, _) => SyncPosition(),
            Dispatcher.CurrentDispatcher);
        _timer.Start();

        OpenFolderCommand = new RelayCommand(OpenFolder);
        PlayPauseCommand = new RelayCommand(PlayPause);
        NextCommand = new RelayCommand(() => Next(auto: false));
        PreviousCommand = new RelayCommand(Previous);
        ShuffleCommand = new RelayCommand(() => IsShuffle = !IsShuffle);
        RepeatCommand = new RelayCommand(() => Repeat = (RepeatMode)(((int)Repeat + 1) % 3));
        MuteCommand = new RelayCommand(() => IsMuted = !IsMuted);
        ToggleDownloadCommand = new RelayCommand(ToggleDownload);
        DownloadCommand = new RelayCommand(() =>
        {
            if (IsDownloading) _downloadCts?.Cancel();
            else if (NeedsTools) _ = GetToolsAsync();
            else _ = DownloadAsync();
        });

        if (_settings.Folder is { } folder && Directory.Exists(folder))
            _ = LoadFolderAsync(folder);
    }

    public ICommand OpenFolderCommand { get; }
    public ICommand PlayPauseCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand PreviousCommand { get; }
    public ICommand ShuffleCommand { get; }
    public ICommand RepeatCommand { get; }
    public ICommand MuteCommand { get; }
    public ICommand ToggleDownloadCommand { get; }
    public ICommand DownloadCommand { get; }

    // ---- Library ----

    ObservableCollection<Track> _tracks = [];
    ICollectionView _tracksView;
    public ICollectionView TracksView
    {
        get => _tracksView;
        private set => SetField(ref _tracksView, value);
    }

    string _status = "";
    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    bool _isScanning;
    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (SetField(ref _isScanning, value)) OnPropertyChanged(nameof(IsEmpty));
        }
    }

    public bool IsEmpty => _tracks.Count == 0 && !IsScanning;

    string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value)) TracksView.Refresh();
        }
    }

    Track? _selectedTrack;
    public Track? SelectedTrack
    {
        get => _selectedTrack;
        set => SetField(ref _selectedTrack, value);
    }

    // ---- Now playing ----

    Track? _currentTrack;
    public Track? CurrentTrack
    {
        get => _currentTrack;
        private set
        {
            var previous = _currentTrack;
            if (!SetField(ref _currentTrack, value)) return;
            if (previous is not null) previous.IsCurrent = false;
            if (value is not null) value.IsCurrent = true;
            OnPropertyChanged(nameof(HasTrack));
            OnPropertyChanged(nameof(WindowTitle));
        }
    }

    public bool HasTrack => CurrentTrack is not null;

    public string WindowTitle => CurrentTrack is { } t
        ? (t.Artist.Length > 0 ? $"{t.Title} · {t.Artist}" : t.Title) + " — Rain"
        : "Rain";

    ImageSource? _cover;
    public ImageSource? Cover
    {
        get => _cover;
        private set => SetField(ref _cover, value);
    }

    bool _isPlaying;
    public bool IsPlaying
    {
        get => _isPlaying;
        private set
        {
            if (SetField(ref _isPlaying, value)) OnPropertyChanged(nameof(PlayGlyph));
        }
    }

    public string PlayGlyph => IsPlaying ? PauseGlyphText : PlayGlyphText;

    double _duration;
    public double Duration
    {
        get => _duration;
        private set
        {
            if (SetField(ref _duration, value)) OnPropertyChanged(nameof(DurationText));
        }
    }

    public string DurationText => Format.Time(TimeSpan.FromSeconds(Duration));

    double _position;
    /// <summary>Seconds. Set by the seek slider; the timer updates the field directly so it never seeks.</summary>
    public double Position
    {
        get => _position;
        set
        {
            if (!SetField(ref _position, value)) return;
            OnPropertyChanged(nameof(PositionText));
            if (!IsSeeking && HasTrack && Math.Abs(_player.Position.TotalSeconds - value) > 0.3)
                _player.Position = TimeSpan.FromSeconds(value);
        }
    }

    public string PositionText => Format.Time(TimeSpan.FromSeconds(Position));

    bool _isSeeking;
    /// <summary>True while the seek thumb is dragged; the seek happens on release.</summary>
    public bool IsSeeking
    {
        get => _isSeeking;
        set
        {
            if (!SetField(ref _isSeeking, value)) return;
            if (!value && HasTrack) _player.Position = TimeSpan.FromSeconds(_position);
        }
    }

    // ---- Volume / modes ----

    double _volume;
    public double Volume
    {
        get => _volume;
        set
        {
            if (!SetField(ref _volume, value)) return;
            if (value > 0 && IsMuted) IsMuted = false;
            ApplyVolume();
        }
    }

    bool _isMuted;
    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (!SetField(ref _isMuted, value)) return;
            ApplyVolume();
        }
    }

    public string VolumeGlyph => IsMuted || Volume == 0 ? ""
        : Volume < 0.34 ? ""
        : Volume < 0.67 ? ""
        : "";

    bool _isShuffle;
    public bool IsShuffle
    {
        get => _isShuffle;
        set
        {
            if (!SetField(ref _isShuffle, value)) return;
            _history.Clear();
            _settings.Shuffle = value;
        }
    }

    RepeatMode _repeat;
    public RepeatMode Repeat
    {
        get => _repeat;
        set
        {
            if (!SetField(ref _repeat, value)) return;
            OnPropertyChanged(nameof(IsRepeatOn));
            OnPropertyChanged(nameof(RepeatGlyph));
            OnPropertyChanged(nameof(RepeatTip));
            _settings.Repeat = value;
        }
    }

    public bool IsRepeatOn => Repeat != RepeatMode.Off;
    public string RepeatGlyph => Repeat == RepeatMode.One ? "" : "";
    public string RepeatTip => Repeat switch
    {
        RepeatMode.All => "Repeat: all",
        RepeatMode.One => "Repeat: one",
        _ => "Repeat: off",
    };

    // ---- Download ----

    CancellationTokenSource? _downloadCts;

    bool _isDownloadOpen;
    public bool IsDownloadOpen
    {
        get => _isDownloadOpen;
        private set => SetField(ref _isDownloadOpen, value);
    }

    string _downloadUrl = "";
    public string DownloadUrl
    {
        get => _downloadUrl;
        set => SetField(ref _downloadUrl, value);
    }

    bool _isDownloading;
    public bool IsDownloading
    {
        get => _isDownloading;
        private set
        {
            if (SetField(ref _isDownloading, value)) OnPropertyChanged(nameof(DownloadButtonText));
        }
    }

    public string DownloadButtonText => IsDownloading ? "Cancel" : NeedsTools ? "Get tools" : "Download";

    List<Tools.Tool> _missingTools = [];
    public bool NeedsTools => _missingTools.Count > 0;

    void RefreshTools()
    {
        _missingTools = Tools.Missing();
        OnPropertyChanged(nameof(NeedsTools));
        OnPropertyChanged(nameof(DownloadButtonText));
        if (!NeedsTools) return;

        var names = string.Join(", ", _missingTools.Select(t => t.Name));
        var size = _missingTools.Sum(t => t.SizeMb);
        DownloadStatus = $"Downloading songs needs {names} (about {size} MB, one time). Want Rain to get {(_missingTools.Count == 1 ? "it" : "them")}?";
    }

    async Task GetToolsAsync()
    {
        var cts = _downloadCts = new CancellationTokenSource();
        IsDownloading = true;
        DownloadProgress = 0;
        var progress = new Progress<(string Tool, double Percent)>(p =>
        {
            if (!IsDownloading) return;
            DownloadProgress = p.Percent;
            DownloadStatus = $"Getting {p.Tool}… {p.Percent:0}%";
        });

        var ready = false;
        try
        {
            await Tools.InstallMissingAsync(progress, cts.Token);
            DownloadStatus = "Tools ready.";
            ready = true;
        }
        catch (OperationCanceledException)
        {
            DownloadStatus = "Cancelled.";
        }
        catch (Exception ex)
        {
            DownloadStatus = $"Couldn't get the tools: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
            cts.Dispose();
            if (_downloadCts == cts) _downloadCts = null;
        }

        var status = DownloadStatus;
        RefreshTools();
        if (NeedsTools) DownloadStatus = status + " " + DownloadStatus;
        else if (ready && DownloadUrl.Trim().Length > 0) await DownloadAsync();
    }

    double _downloadProgress;
    public double DownloadProgress
    {
        get => _downloadProgress;
        private set => SetField(ref _downloadProgress, value);
    }

    string _downloadStatus = "";
    public string DownloadStatus
    {
        get => _downloadStatus;
        private set
        {
            if (SetField(ref _downloadStatus, value)) OnPropertyChanged(nameof(HasDownloadStatus));
        }
    }

    public bool HasDownloadStatus => DownloadStatus.Length > 0;

    void ToggleDownload()
    {
        IsDownloadOpen = !IsDownloadOpen;
        if (!IsDownloadOpen || IsDownloading) return;

        RefreshTools();
        if (DownloadUrl.Length > 0) return;

        // Pre-fill a link that's already on the clipboard.
        try
        {
            var text = Clipboard.GetText().Trim();
            if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                DownloadUrl = text;
        }
        catch (Exception) { }
    }

    async Task DownloadAsync()
    {
        var url = DownloadUrl.Trim();
        if (url.Length == 0) return;

        if (_settings.Folder is not { } folder || !Directory.Exists(folder))
        {
            DownloadStatus = "Pick your music folder first, downloads go there.";
            OpenFolder();
            if (_settings.Folder is null) return;
            folder = _settings.Folder;
        }

        var cts = _downloadCts = new CancellationTokenSource();
        IsDownloading = true;
        DownloadProgress = 0;
        DownloadStatus = "Starting…";
        var progress = new Progress<double>(p =>
        {
            if (!IsDownloading) return;
            if (p < 0)
            {
                DownloadProgress = 100;
                DownloadStatus = "Converting and adding cover…";
            }
            else
            {
                DownloadProgress = p;
                DownloadStatus = $"Downloading… {p:0}%";
            }
        });

        try
        {
            var path = await Downloader.DownloadAsync(url, folder, progress, cts.Token);
            var track = await Task.Run(() => Library.Read(path));
            AddToLibrary(track);
            DownloadStatus = $"Added \"{track.Title}\"";
            DownloadUrl = "";
        }
        catch (OperationCanceledException)
        {
            DownloadStatus = "Cancelled.";
        }
        catch (Exception ex)
        {
            DownloadStatus = $"Download failed: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
            cts.Dispose();
            if (_downloadCts == cts) _downloadCts = null;
        }
    }

    void AddToLibrary(Track track)
    {
        // Downloading the same song again replaces its row instead of duplicating it.
        var existing = _tracks.FirstOrDefault(t => string.Equals(t.Path, track.Path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (existing == CurrentTrack)
            {
                SelectedTrack = existing;
                return;
            }
            _tracks.Remove(existing);
        }

        var index = 0;
        while (index < _tracks.Count && Library.Compare(_tracks[index], track) <= 0) index++;
        _tracks.Insert(index, track);

        SelectedTrack = track;
        Status = TrackCountText();
        OnPropertyChanged(nameof(IsEmpty));
    }

    // ---- Actions ----

    public void Play(Track track) => Play(track, remember: true);

    void Play(Track track, bool remember)
    {
        try
        {
            _player.Open(track.Path);
        }
        catch (Exception ex)
        {
            IsPlaying = false;
            Status = $"Can't play \"{track.Title}\": {ex.Message}";
            return;
        }

        if (remember && CurrentTrack is not null && CurrentTrack != track)
            _history.Push(CurrentTrack);

        CurrentTrack = track;
        SelectedTrack = track;

        // Reset position before the slider's maximum changes, so it never coerces into a seek.
        SetPositionFromPlayer();
        Duration = _player.Duration.TotalSeconds;

        _player.Play();
        IsPlaying = true;
        Status = TrackCountText();
        _ = LoadCoverAsync(track);
    }

    void PlayPause()
    {
        if (CurrentTrack is null)
        {
            var first = SelectedTrack ?? VisibleTracks().FirstOrDefault();
            if (first is not null) Play(first);
            return;
        }

        if (IsPlaying)
        {
            _player.Pause();
            IsPlaying = false;
        }
        else
        {
            _player.Play();
            IsPlaying = true;
        }
    }

    void Next(bool auto)
    {
        if (auto && Repeat == RepeatMode.One && CurrentTrack is not null)
        {
            Play(CurrentTrack, remember: false);
            return;
        }

        var list = VisibleTracks();
        if (list.Count == 0) return;

        Track? next;
        if (IsShuffle)
        {
            next = list.Count == 1 ? list[0] : list.Where(t => t != CurrentTrack).ElementAt(_random.Next(list.Count - 1));
        }
        else
        {
            var index = CurrentTrack is null ? -1 : list.IndexOf(CurrentTrack);
            if (index + 1 < list.Count) next = list[index + 1];
            else if (!auto || Repeat == RepeatMode.All) next = list[0];
            else next = null;
        }

        if (next is not null)
        {
            Play(next);
            return;
        }

        // Reached the end with repeat off: rewind and stop.
        _player.Position = TimeSpan.Zero;
        IsPlaying = false;
        SetPositionFromPlayer();
    }

    void Previous()
    {
        if (CurrentTrack is null) return;

        if (_player.Position.TotalSeconds > 3)
        {
            _player.Position = TimeSpan.Zero;
            SetPositionFromPlayer();
            return;
        }

        if (IsShuffle && _history.Count > 0)
        {
            Play(_history.Pop(), remember: false);
            return;
        }

        var list = VisibleTracks();
        if (list.Count == 0) return;
        var index = list.IndexOf(CurrentTrack);
        Play(list[index <= 0 ? list.Count - 1 : index - 1], remember: false);
    }

    void OpenFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Pick your music folder" };
        if (_settings.Folder is { } current && Directory.Exists(current))
            dialog.InitialDirectory = current;

        if (dialog.ShowDialog() != true) return;

        _settings.Folder = dialog.FolderName;
        _settings.Save();
        _ = LoadFolderAsync(dialog.FolderName);
    }

    async Task LoadFolderAsync(string folder)
    {
        _scanCts?.Cancel();
        var cts = _scanCts = new CancellationTokenSource();

        IsScanning = true;
        Status = "Scanning…";
        var progress = new Progress<int>(n => Status = $"Scanning… {n} tracks");

        try
        {
            var tracks = await Task.Run(() => Library.Scan(folder, progress, cts.Token), cts.Token);

            _tracks = new ObservableCollection<Track>(tracks);
            TracksView = CreateView(_tracks);

            // Keep the playing track highlighted if it's in the new list.
            if (CurrentTrack is { } playing)
            {
                var match = _tracks.FirstOrDefault(t => string.Equals(t.Path, playing.Path, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                {
                    playing.IsCurrent = false;
                    _currentTrack = match;
                    match.IsCurrent = true;
                    OnPropertyChanged(nameof(CurrentTrack));
                }
            }

            Status = TrackCountText();
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Status = $"Couldn't read that folder: {ex.Message}";
        }

        if (cts == _scanCts)
        {
            IsScanning = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    async Task LoadCoverAsync(Track track)
    {
        Cover = null;
        var image = await Task.Run(() => Library.ReadCover(track.Path, 160));
        if (CurrentTrack == track) Cover = image;
    }

    // ---- Helpers ----

    ICollectionView CreateView(ObservableCollection<Track> tracks)
    {
        var view = CollectionViewSource.GetDefaultView(tracks);
        view.Filter = item => Matches((Track)item);
        return view;
    }

    bool Matches(Track track)
    {
        if (string.IsNullOrWhiteSpace(SearchText)) return true;

        foreach (var term in SearchText.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!track.Title.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                && !track.Artist.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                && !track.Album.Contains(term, StringComparison.CurrentCultureIgnoreCase))
                return false;
        }
        return true;
    }

    List<Track> VisibleTracks() => TracksView.Cast<Track>().ToList();

    string TrackCountText() => _tracks.Count == 1 ? "1 track" : $"{_tracks.Count} tracks";

    void ApplyVolume()
    {
        _player.Volume = IsMuted ? 0f : (float)Volume;
        _settings.Volume = Volume;
        OnPropertyChanged(nameof(VolumeGlyph));
    }

    void SyncPosition()
    {
        if (!HasTrack || IsSeeking) return;
        SetPositionFromPlayer();
    }

    void SetPositionFromPlayer()
    {
        _position = _player.Position.TotalSeconds;
        OnPropertyChanged(nameof(Position));
        OnPropertyChanged(nameof(PositionText));
    }

    public void Dispose()
    {
        _timer.Stop();
        _scanCts?.Cancel();
        _downloadCts?.Cancel();
        _settings.Save();
        _player.Dispose();
    }
}
