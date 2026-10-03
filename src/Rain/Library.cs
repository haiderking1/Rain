using System.IO;
using System.Windows.Media.Imaging;

namespace Rain;

public static class Library
{
    static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".wav", ".m4a", ".aac", ".wma", ".aif", ".aiff",
    };

    static readonly string[] CoverFiles = ["cover.jpg", "cover.png", "folder.jpg", "folder.png", "front.jpg", "front.png"];

    public static List<Track> Scan(string folder, IProgress<int>? progress, CancellationToken ct)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        var tracks = new List<Track>();

        foreach (var path in Directory.EnumerateFiles(folder, "*", options))
        {
            ct.ThrowIfCancellationRequested();
            if (!Extensions.Contains(Path.GetExtension(path))) continue;

            tracks.Add(Read(path));
            if (tracks.Count % 50 == 0) progress?.Report(tracks.Count);
        }

        tracks.Sort(Compare);
        return tracks;
    }

    /// <summary>Library order: artist, album, disc, track number, title.</summary>
    public static int Compare(Track a, Track b)
    {
        var byText = StringComparer.CurrentCultureIgnoreCase;
        int c;
        if ((c = byText.Compare(a.Artist, b.Artist)) != 0) return c;
        if ((c = byText.Compare(a.Album, b.Album)) != 0) return c;
        if ((c = a.Disc.CompareTo(b.Disc)) != 0) return c;
        if ((c = a.Number.CompareTo(b.Number)) != 0) return c;
        if ((c = byText.Compare(a.Title, b.Title)) != 0) return c;
        return StringComparer.OrdinalIgnoreCase.Compare(a.Path, b.Path);
    }

    public static Track Read(string path)
    {
        var fallback = Path.GetFileNameWithoutExtension(path);
        try
        {
            using var file = TagLib.File.Create(path);
            var tag = file.Tag;
            return new Track
            {
                Path = path,
                Title = string.IsNullOrWhiteSpace(tag.Title) ? fallback : tag.Title.Trim(),
                Artist = FirstNonEmpty(tag.JoinedPerformers, tag.JoinedAlbumArtists),
                Album = tag.Album?.Trim() ?? "",
                Disc = tag.Disc,
                Number = tag.Track,
                Duration = file.Properties?.Duration ?? TimeSpan.Zero,
            };
        }
        catch (Exception)
        {
            // Unreadable or untagged file: still list it, by file name.
            return new Track { Path = path, Title = fallback };
        }
    }

    /// <summary>Embedded art first, then a cover image next to the file.</summary>
    public static BitmapImage? ReadCover(string path, int size)
    {
        try
        {
            using (var file = TagLib.File.Create(path))
            {
                var picture = file.Tag.Pictures.FirstOrDefault();
                if (picture is not null)
                    return Decode(new MemoryStream(picture.Data.Data), size);
            }
        }
        catch (Exception) { }

        var dir = Path.GetDirectoryName(path);
        if (dir is null) return null;

        foreach (var name in CoverFiles)
        {
            var coverPath = Path.Combine(dir, name);
            if (!File.Exists(coverPath)) continue;
            try
            {
                return Decode(File.OpenRead(coverPath), size);
            }
            catch (Exception) { }
        }
        return null;
    }

    static BitmapImage Decode(Stream stream, int size)
    {
        using (stream)
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = size;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
    }

    static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? "";
}
