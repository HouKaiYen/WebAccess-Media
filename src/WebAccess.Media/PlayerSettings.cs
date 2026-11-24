using System.Text.Json;

namespace WebAccess.Media;

public sealed record PlaylistItem(string File, string Name, int Seconds)
{
    public bool IsVideo => File.EndsWith(".mp4", StringComparison.Ordinal);
}

public sealed record PointPlan(string Point, string File, int X, int Y,
    string Icon = PointPlan.DefaultIcon, bool Flash = true, bool ImageOnly = false, string? IconFile = null,
    string? AlarmText = null, int AlarmTextSize = PointPlan.DefaultAlarmTextSize, int IconSize = PointPlan.DefaultIconSize)
{
    public const string DefaultIcon = "sos";
    public const int DefaultAlarmTextSize = 48;
    public const int DefaultIconSize = 36;
    public const int MinAlarmTextSize = 16;
    public const int MaxAlarmTextSize = 120;
    public const int MinIconSize = 20;
    public const int MaxIconSize = 160;
}
public sealed record FloorImage(string File, string Name);

public sealed record Playlist(int Revision, PlaylistItem[] Items, bool IsMuted, string Marquee, bool MarqueeAtTop,
    PointPlan[]? PointPlans = null, FloorImage[]? FloorImages = null);

public sealed class PlayerSettings(string directory)
{
    private readonly string _path = Path.Combine(directory, "playlist.json");
    private readonly string _mediaDirectory = Path.Combine(directory, "media");
    private readonly string _engineeringDirectory = Path.Combine(directory, "engineering-media");
    private readonly object _gate = new();

    public Playlist Read()
    {
        lock (_gate)
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<Playlist>(File.ReadAllText(_path)) ?? throw new InvalidDataException()
                : new Playlist(0, [], true, "", false);
        }
    }

    public bool Save(Playlist playlist)
    {
        lock (_gate)
        {
            if (Read().Revision != playlist.Revision)
            {
                return false;
            }

            Directory.CreateDirectory(directory);
            string temporaryPath = Path.Combine(directory, $"{Guid.NewGuid():N}.tmp");
            try
            {
                Playlist saved = playlist with { Revision = playlist.Revision + 1 };
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(saved));
                File.Move(temporaryPath, _path, overwrite: true);
                return true;
            }
            finally
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public Task<string> UploadAsync(IFormFile upload, CancellationToken cancellationToken) =>
        UploadAsync(upload, _mediaDirectory, cancellationToken);

    public Task<string> UploadEngineeringAsync(IFormFile upload, CancellationToken cancellationToken) =>
        UploadAsync(upload, _engineeringDirectory, cancellationToken);

    private static async Task<string> UploadAsync(IFormFile upload, string targetDirectory, CancellationToken cancellationToken)
    {
        string extension = Path.GetExtension(upload.FileName).ToLowerInvariant();
        long limit = (extension == ".mp4" ? 100L : 20L) * 1024 * 1024;
        if (MimeType(extension) is null || upload.Length < 12 || upload.Length > limit)
        {
            throw new InvalidDataException("請上傳 PNG、JPEG、GIF 或 MP4，並確認檔案大小。");
        }

        await using Stream input = upload.OpenReadStream();
        byte[] header = new byte[12];
        await input.ReadExactlyAsync(header, cancellationToken);
        bool valid = extension switch
        {
            ".png" => header.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            ".jpg" or ".jpeg" => header[0] == 255 && header[1] == 216 && header[2] == 255,
            ".gif" => header.AsSpan(0, 6).SequenceEqual("GIF87a"u8) || header.AsSpan(0, 6).SequenceEqual("GIF89a"u8),
            ".mp4" => header.AsSpan(4, 4).SequenceEqual("ftyp"u8),
            _ => false
        };
        if (!valid)
        {
            throw new InvalidDataException("檔案內容與選取的媒體格式不符。");
        }

        Directory.CreateDirectory(targetDirectory);
        string file = $"{Guid.NewGuid():N}{extension}";
        string path = Path.Combine(targetDirectory, file);
        try
        {
            await using FileStream output = File.Open(path, FileMode.CreateNew, FileAccess.Write);
            await output.WriteAsync(header, cancellationToken);
            await input.CopyToAsync(output, cancellationToken);
            return file;
        }
        catch
        {
            File.Delete(path);
            throw;
        }
    }

    public string? FindMedia(string file)
    {
        Playlist playlist = Read();
        return IsEngineeringReference(playlist, file) && !playlist.Items.Any(item => item.File == file)
            ? null : FindMedia(_mediaDirectory, file);
    }

    public string? FindEngineeringMedia(string file) =>
        FindMedia(_engineeringDirectory, file) ??
        (IsEngineeringReference(Read(), file) ? FindMedia(_mediaDirectory, file) : null);

    private static bool IsEngineeringReference(Playlist playlist, string file) =>
        (playlist.FloorImages?.Any(image => image.File == file) ?? false) ||
        (playlist.PointPlans?.Any(plan => plan.File == file || plan.IconFile == file) ?? false);

    private static string? FindMedia(string targetDirectory, string file)
    {
        if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out _) ||
            MimeType(Path.GetExtension(file)) is null || Path.GetFileName(file) != file)
        {
            return null;
        }

        string path = Path.Combine(targetDirectory, file);
        return File.Exists(path) ? path : null;
    }

    public void RemoveMedia(string file)
    {
        if (FindMedia(_mediaDirectory, file) is string path)
        {
            File.Delete(path);
        }
    }

    public void RemoveEngineeringMedia(string file)
    {
        if (FindMedia(_engineeringDirectory, file) is string path)
        {
            File.Delete(path);
        }
    }

    public static string? MimeType(string extension) => extension switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".mp4" => "video/mp4",
        _ => null
    };
}
