using System.Text.Json;

namespace WebAccess.Media;

public sealed record PlaylistItem(string File, string Name, int Seconds)
{
    public bool IsVideo => File.EndsWith(".mp4", StringComparison.Ordinal);
}

public sealed record PointPlan(string Point, string File, int X, int Y,
    string Icon = PointPlan.DefaultIcon, bool Flash = true, bool ImageOnly = false, string? IconFile = null,
    string? AlarmText = null, int AlarmTextSize = PointPlan.DefaultAlarmTextSize, int IconSize = PointPlan.DefaultIconSize,
    bool AlarmSoundEnabled = false, string AlarmTone = PointPlan.DefaultAlarmTone, int AlarmVolume = PointPlan.DefaultAlarmVolume,
    string? AlarmAudioFile = null, string? AlarmAudioName = null)
{
    public const string DefaultIcon = "sos";
    public const int DefaultCoordinate = 50;
    public const int DefaultAlarmTextSize = 48;
    public const int DefaultIconSize = 36;
    public const int MinAlarmTextSize = 16;
    public const int MaxAlarmTextSize = 120;
    public const int MinIconSize = 20;
    public const int MaxIconSize = 160;
    public const string DefaultAlarmTone = "chime";
    public const string CustomAlarmTone = "custom";
    public const int DefaultAlarmVolume = 70;
    public const int MinAlarmVolume = 1;
    public const int MaxAlarmVolume = 100;
}
public sealed record FloorImage(string File, string Name);
public sealed record StandbyMedia(string File, string Name);

public sealed record Playlist(int Revision, PlaylistItem[] Items, bool IsMuted, string Marquee, bool MarqueeAtTop,
    PointPlan[]? PointPlans = null, FloorImage[]? FloorImages = null,
    int MarqueeFontSize = Playlist.DefaultMarqueeFontSize, int MarqueeSpeed = Playlist.DefaultMarqueeSpeed,
    StandbyMedia[]? MediaLibrary = null)
{
    public const int DefaultMarqueeFontSize = 30;
    public const int MinMarqueeFontSize = 12;
    public const int MaxMarqueeFontSize = 120;
    public const int DefaultMarqueeSpeed = 80;
    public const int MinMarqueeSpeed = 10;
    public const int MaxMarqueeSpeed = 500;
}

public sealed class PlayerSettings(string directory)
{
    public const int MaxAlarmAudioMegabytes = 10;
    private readonly string _path = Path.Combine(directory, "playlist.json");
    private readonly string _mediaDirectory = Path.Combine(directory, "media");
    private readonly string _engineeringDirectory = Path.Combine(directory, "engineering-media");
    private readonly string _alarmAudioDirectory = Path.Combine(directory, "alarm-audio");
    private readonly object _gate = new();

    public Playlist Read()
    {
        lock (_gate)
        {
            Playlist playlist = File.Exists(_path)
                ? JsonSerializer.Deserialize<Playlist>(File.ReadAllText(_path)) ?? throw new InvalidDataException()
                : new Playlist(0, [], true, "", false);
            return playlist.MediaLibrary is null
                ? playlist with { MediaLibrary = playlist.Items.Select(item => new StandbyMedia(item.File, item.Name)).ToArray() }
                : playlist;
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

        return await WriteUploadAsync(input, header, targetDirectory, extension, cancellationToken);
    }

    public async Task<string> UploadAlarmAudioAsync(IFormFile upload, CancellationToken cancellationToken)
    {
        string extension = Path.GetExtension(upload.FileName).ToLowerInvariant();
        if (AlarmAudioMimeType(extension) is null || upload.Length < 12 ||
            upload.Length > MaxAlarmAudioMegabytes * 1024L * 1024)
        {
            throw new InvalidDataException($"請上傳 {MaxAlarmAudioMegabytes} MB 以內的 MP3 或 WAV 音檔。");
        }

        await using Stream input = upload.OpenReadStream();
        byte[] header = new byte[12];
        await input.ReadExactlyAsync(header, cancellationToken);
        bool valid = extension == ".wav"
            ? header.AsSpan(0, 4).SequenceEqual("RIFF"u8) && header.AsSpan(8, 4).SequenceEqual("WAVE"u8)
            : header.AsSpan(0, 3).SequenceEqual("ID3"u8) ||
                (header[0] == 0xff && (header[1] & 0xe0) == 0xe0 && (header[1] & 0x06) == 0x02);
        if (!valid)
        {
            throw new InvalidDataException("音檔內容與選取的格式不符。");
        }

        return await WriteUploadAsync(input, header, _alarmAudioDirectory, extension, cancellationToken);
    }

    private static async Task<string> WriteUploadAsync(Stream input, byte[] header, string targetDirectory, string extension,
        CancellationToken cancellationToken)
    {
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

    public string? FindAlarmAudio(string file) =>
        IsAlarmAudioReference(Read(), file) ? FindAlarmAudioPath(file) : null;

    public bool RemoveUnusedAlarmAudio(string? file)
    {
        if (file is null) return true;
        lock (_gate)
        {
            if (IsAlarmAudioReference(Read(), file) || FindAlarmAudioPath(file) is not string path)
            {
                return true;
            }
            try
            {
                File.Delete(path);
                return true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    private static bool IsAlarmAudioReference(Playlist playlist, string file) =>
        playlist.PointPlans?.Any(plan => string.Equals(plan.AlarmAudioFile, file, StringComparison.OrdinalIgnoreCase)) ?? false;

    private string? FindAlarmAudioPath(string file) =>
        AlarmAudioMimeType(Path.GetExtension(file)) is null ? null : FindStoredFile(_alarmAudioDirectory, file);

    private static bool IsEngineeringReference(Playlist playlist, string file) =>
        (playlist.FloorImages?.Any(image => string.Equals(image.File, file, StringComparison.OrdinalIgnoreCase)) ?? false) ||
        (playlist.PointPlans?.Any(plan => string.Equals(plan.File, file, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(plan.IconFile, file, StringComparison.OrdinalIgnoreCase)) ?? false);

    public StandbyMedia[] GetStandbyMedia(Playlist playlist) =>
        (playlist.MediaLibrary ?? []).Where(media => !IsEngineeringReference(playlist, media.File) ||
            IsInPlaylist(playlist, media.File)).ToArray();

    public StandbyMediaDeletion DeleteStandbyMedia(string? file, int revision)
    {
        lock (_gate)
        {
            Playlist playlist = Read();
            if (playlist.Revision != revision)
            {
                return StandbyMediaDeletion.Stale;
            }

            StandbyMedia? media = GetStandbyMedia(playlist).FirstOrDefault(item => item.File == file);
            if (media is null)
            {
                return StandbyMediaDeletion.NotFound;
            }
            if (IsInPlaylist(playlist, media.File) || IsEngineeringReference(playlist, media.File))
            {
                return StandbyMediaDeletion.InUse;
            }

            RemoveMedia(media.File);
            StandbyMedia[] remaining = (playlist.MediaLibrary ?? []).Where(item => item.File != media.File).ToArray();
            return Save(playlist with { MediaLibrary = remaining }) ? StandbyMediaDeletion.Deleted : StandbyMediaDeletion.Stale;
        }
    }

    private static bool IsInPlaylist(Playlist playlist, string file) =>
        playlist.Items.Any(item => string.Equals(item.File, file, StringComparison.OrdinalIgnoreCase));

    public void RemoveUnusedMedia(string file, bool isEngineeringMedia = false)
    {
        lock (_gate)
        {
            Playlist playlist = Read();
            if (IsEngineeringReference(playlist, file) || IsInPlaylist(playlist, file))
            {
                return;
            }

            string? path = isEngineeringMedia ? FindMedia(_engineeringDirectory, file) : null;
            if (path is null && !(playlist.MediaLibrary?.Any(media => string.Equals(media.File, file, StringComparison.OrdinalIgnoreCase)) ?? false))
            {
                path = FindMedia(_mediaDirectory, file);
            }
            if (path is not null)
            {
                File.Delete(path);
            }
        }
    }

    private static string? FindMedia(string targetDirectory, string file) =>
        MimeType(Path.GetExtension(file)) is null ? null : FindStoredFile(targetDirectory, file);

    private static string? FindStoredFile(string targetDirectory, string file)
    {
        if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out _) ||
            Path.GetFileName(file) != file)
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

    public static string? AlarmAudioMimeType(string extension) => extension switch
    {
        ".mp3" => "audio/mpeg",
        ".wav" => "audio/wav",
        _ => null
    };
}

public enum StandbyMediaDeletion
{
    Deleted,
    Stale,
    NotFound,
    InUse
}
