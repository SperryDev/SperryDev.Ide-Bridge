using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TokenSaver.LogHelper.State;

/// <summary>
/// Stores the last few run snapshots per key as one JSON file each under the state directory.
/// Writes go to a temp file that is moved over the target, so a reader never sees a torn file and a
/// first write needs no existing file. A corrupt file is renamed to <c>.corrupt</c> and treated as empty.
/// </summary>
public sealed class RunSnapshotStore
{
    private const int MaxNameChars = 80;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _stateDirectory;
    private readonly int _retention;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public RunSnapshotStore(string stateDirectory, int retention = 5)
    {
        _stateDirectory = stateDirectory;
        _retention = retention;
    }

    public async Task AppendAsync(string key, RunSnapshot snapshot, CancellationToken ct)
    {
        SemaphoreSlim gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            List<RunSnapshot> runs = (await ReadAsync(PathFor(key), ct)).ToList();
            runs.Add(snapshot);
            if (runs.Count > _retention)
            {
                runs.RemoveRange(0, runs.Count - _retention);
            }

            await WriteAtomicAsync(PathFor(key), runs, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<RunSnapshot>> ListAsync(string key, CancellationToken ct)
    {
        SemaphoreSlim gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            return await ReadAsync(PathFor(key), ct);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Maps a key such as <c>file:C:\logs\a.log</c> to a safe file name. A hash suffix keeps distinct keys apart.</summary>
    public static string SafeFileName(string key)
    {
        var sb = new StringBuilder(Math.Min(key.Length, MaxNameChars));
        foreach (char c in key)
        {
            if (sb.Length >= MaxNameChars)
            {
                break;
            }

            sb.Append(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_');
        }

        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return sb + "-" + Convert.ToHexString(digest, 0, 4).ToLowerInvariant();
    }

    private string PathFor(string key) =>
        Path.Combine(_stateDirectory, SafeFileName(key) + ".json");

    private static async Task<IReadOnlyList<RunSnapshot>> ReadAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            return Array.Empty<RunSnapshot>();
        }

        string text = await File.ReadAllTextAsync(path, ct);
        try
        {
            List<RunSnapshot>? runs = JsonSerializer.Deserialize<List<RunSnapshot>>(text, JsonOptions);
            return runs ?? new List<RunSnapshot>();
        }
        catch (JsonException)
        {
            File.Move(path, path + ".corrupt", overwrite: true);
            return Array.Empty<RunSnapshot>();
        }
    }

    private async Task WriteAtomicAsync(string path, IReadOnlyList<RunSnapshot> runs, CancellationToken ct)
    {
        Directory.CreateDirectory(_stateDirectory);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(runs, JsonOptions), ct);
        File.Move(temp, path, overwrite: true);
    }
}
