namespace Rain;

public sealed class Track : ObservableObject
{
    public required string Path { get; init; }
    public required string Title { get; init; }
    public string Artist { get; init; } = "";
    public string Album { get; init; } = "";
    public uint Disc { get; init; }
    public uint Number { get; init; }
    public TimeSpan Duration { get; init; }

    public string DurationText => Format.Time(Duration);

    bool _isCurrent;
    public bool IsCurrent
    {
        get => _isCurrent;
        set => SetField(ref _isCurrent, value);
    }
}

public static class Format
{
    public static string Time(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
}
