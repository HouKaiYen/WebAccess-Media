using System.Text.Json;

namespace WebAccess.Media;

public sealed record PlaylistItem(string File, string Name, int Seconds)
{
    public bool IsVideo => File.EndsWith(".mp4", StringComparison.Ordinal);
}

public sealed record PointPlan(string Point, string File, int X, int Y);

public sealed record Playlist(int Revision, PlaylistItem[] Items, bool IsMuted, string Marquee, bool MarqueeAtTop,
    PointPlan[]? PointPlans = null);

public sealed class PlayerSettings(string directory)
{
    private readonly string _path = Path.Combine(directory, "playlist.json");
    private readonly string _mediaDirectory = Path.Combine(directory, "media");
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

    public async Task<string> UploadAsync(IFormFile upload, CancellationToken cancellationToken)
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

        Directory.CreateDirectory(_mediaDirectory);
        string file = $"{Guid.NewGuid():N}{extension}";
        string path = Path.Combine(_mediaDirectory, file);
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
        if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out _) ||
            MimeType(Path.GetExtension(file)) is null || Path.GetFileName(file) != file)
        {
            return null;
        }

        string path = Path.Combine(_mediaDirectory, file);
        return File.Exists(path) ? path : null;
    }

    public void RemoveMedia(string file)
    {
        if (FindMedia(file) is string path)
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
