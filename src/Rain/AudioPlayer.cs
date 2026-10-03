using NAudio.Wave;

namespace Rain;

public sealed class AudioPlayer : IDisposable
{
    WaveOut? _output;
    AudioFileReader? _reader;
    float _volume = 1f;

    /// <summary>Raised when the current track plays to the end (or the device fails).</summary>
    public event EventHandler? TrackEnded;

    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;

    public TimeSpan Duration => _reader?.TotalTime ?? TimeSpan.Zero;

    public TimeSpan Position
    {
        get => _reader?.CurrentTime ?? TimeSpan.Zero;
        set
        {
            if (_reader is null) return;
            var max = _reader.TotalTime - TimeSpan.FromMilliseconds(100);
            _reader.CurrentTime = value < TimeSpan.Zero ? TimeSpan.Zero : value > max ? max : value;
        }
    }

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = value;
            if (_reader is not null) _reader.Volume = value;
        }
    }

    public void Open(string path)
    {
        Close();

        var reader = new AudioFileReader(path) { Volume = _volume };
        var output = new WaveOut();
        try
        {
            output.Init(reader);
        }
        catch
        {
            output.Dispose();
            reader.Dispose();
            throw;
        }

        // Stop events from an output we already replaced are ignored.
        output.PlaybackStopped += (_, _) =>
        {
            if (ReferenceEquals(output, _output)) TrackEnded?.Invoke(this, EventArgs.Empty);
        };

        _reader = reader;
        _output = output;
    }

    public void Play() => _output?.Play();

    public void Pause() => _output?.Pause();

    public void Close()
    {
        var output = _output;
        var reader = _reader;
        _output = null;
        _reader = null;
        output?.Dispose();
        reader?.Dispose();
    }

    public void Dispose() => Close();
}
