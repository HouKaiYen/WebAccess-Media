using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace WebAccess.Media;

public sealed record ConnectionSettings(string ApiBase, string Project, string Username, string Password);

public sealed class EngineeringSettings(
    string directory, IDataProtector protector, WebAccessClient client)
{
    private readonly string _path = Path.Combine(directory, "connection.protected");
    private readonly object _gate = new();
    private ConnectionSettings? _current;

    public ConnectionSettings? Current
    {
        get
        {
            lock (_gate)
            {
                return _current ??= File.Exists(_path)
                    ? JsonSerializer.Deserialize<ConnectionSettings>(protector.Unprotect(File.ReadAllText(_path)))
                    : null;
            }
        }
    }

    public async Task TestAsync(ConnectionSettings settings, CancellationToken cancellationToken)
    {
        await client.GetAlarmsAsync(settings, cancellationToken);
    }

    public async Task SaveAsync(ConnectionSettings settings, CancellationToken cancellationToken)
    {
        await TestAsync(settings, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            Directory.CreateDirectory(directory);
            string temporaryPath = Path.Combine(directory, $"{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(temporaryPath, protector.Protect(JsonSerializer.Serialize(settings)));
                File.Move(temporaryPath, _path, overwrite: true);
                _current = settings;
            }
            finally
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
