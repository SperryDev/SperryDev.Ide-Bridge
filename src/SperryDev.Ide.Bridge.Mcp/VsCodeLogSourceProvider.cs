using SperryDev.Ide.Bridge;
using TokenSaver.LogHelper.Contracts;
using TokenSaver.LogHelper.Models;
using TokenSaver.LogHelper.Sources;

namespace TokenSaver.MCP.Services.LogSources;

/// <summary>
/// Serves <c>vscode:</c> channels: the output-channel files VS Code writes under each reported exthost folder
/// (<c>output_logging_&lt;ts&gt;/N-&lt;name&gt;.log</c>). The channel id carries the full file path. Reads go through
/// <see cref="FileLogSource"/>, allowing only the roots that running VS Code windows reported.
/// </summary>
internal sealed class VsCodeLogSourceProvider : ILogSourceProvider
{
    private readonly Func<IReadOnlyList<VsInstance>> _instances;

    public VsCodeLogSourceProvider(Func<IReadOnlyList<VsInstance>> instances)
    {
        _instances = instances;
    }

    public string Scheme => "vscode";

    public Task<IReadOnlyList<LogChannelInfo>> ListChannelsAsync(CancellationToken ct)
    {
        var channels = new List<LogChannelInfo>();
        foreach (string root in RootsOf(_instances()))
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (string dir in Directory.EnumerateDirectories(root, "output_logging_*"))
            {
                foreach (string file in Directory.EnumerateFiles(dir, "*.log"))
                {
                    channels.Add(new LogChannelInfo(
                        ChannelId.Parse("vscode:" + file),
                        Path.GetFileNameWithoutExtension(file),
                        new FileInfo(file).Length));
                }
            }
        }

        return Task.FromResult<IReadOnlyList<LogChannelInfo>>(channels);
    }

    public ILogSource Open(ChannelId id) =>
        new FileLogSource(id.Name, new LogHelperOptions { AllowedRoots = RootsOf(_instances()) });

    private static List<string> RootsOf(IReadOnlyList<VsInstance> instances) =>
        instances
            .Where(i => string.Equals(i.Ide, "VsCode", StringComparison.Ordinal))
            .SelectMany(i => i.LogRoots)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
