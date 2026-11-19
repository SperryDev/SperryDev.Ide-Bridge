using TokenSaver.LogHelper.Contracts;
using TokenSaver.LogHelper.Models;

namespace TokenSaver.LogHelper.Sources;

/// <summary>
/// Serves <c>file:</c> channels named explicitly by path. <see cref="ListChannelsAsync"/> returns nothing:
/// files are not enumerated, so callers must already know the path.
/// </summary>
public sealed class FileLogSourceProvider : ILogSourceProvider
{
    private readonly LogHelperOptions _options;

    public FileLogSourceProvider(LogHelperOptions options)
    {
        _options = options;
    }

    public string Scheme => "file";

    public Task<IReadOnlyList<LogChannelInfo>> ListChannelsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<LogChannelInfo>>(Array.Empty<LogChannelInfo>());

    public ILogSource Open(ChannelId id) => new FileLogSource(id.Name, _options);
}
