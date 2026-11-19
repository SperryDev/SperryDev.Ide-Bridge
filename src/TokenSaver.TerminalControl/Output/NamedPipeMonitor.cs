namespace TokenSaver.TerminalControl.Output;

using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TokenSaver.TerminalControl.Contracts;

public sealed class NamedPipeMonitor : INamedPipeMonitor
{
    private readonly NamedPipeManager _pipeManager;
    private readonly ILogger<NamedPipeMonitor> _logger;

    public NamedPipeMonitor(
        NamedPipeManager pipeManager,
        ILogger<NamedPipeMonitor> logger)
    {
        _pipeManager = pipeManager;
        _logger = logger;
    }

    public async IAsyncEnumerable<string> MonitorPipeAsync(
        string pipeName,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Monitoring pipe {PipeName}", pipeName);
        }

        var pipe = _pipeManager.GetPipe(pipeName);
        if (pipe is null)
        {
            _logger.LogWarning("Pipe {PipeName} not found", pipeName);
            yield break;
        }

        await foreach (var line in pipe.ReadLinesAsync())
        {
            yield return line;
        }
    }

    public async Task<string> ReadPipeOutputAsync(string pipeName, CancellationToken ct = default)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Reading output from pipe {PipeName}", pipeName);
        }

        try
        {
            return await _pipeManager.ReadPipeOutputAsync(pipeName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading pipe {PipeName}", pipeName);
            return string.Empty;
        }
    }

    public Task<long> GetPipeSizeAsync(string pipeName, CancellationToken ct = default)
    {
        var size = _pipeManager.GetPipeSize(pipeName);
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Pipe {PipeName} size: {Size} bytes", pipeName, size);
        }
        return Task.FromResult(size);
    }

    public async IAsyncEnumerable<(string PipeName, string Output)> MonitorMultiplePipesAsync(
        IEnumerable<string> pipeNames,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var pipeList = pipeNames.ToList();
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Monitoring {PipeCount} pipes", pipeList.Count);
        }

        // Simplified: just yield from each in sequence
        // For true multiplexing, would need more sophisticated task orchestration
        foreach (var pipeName in pipeList)
        {
            await foreach (var line in MonitorPipeAsync(pipeName, ct))
            {
                yield return (pipeName, line);
            }
        }
    }

    public async Task<bool> SearchPipeOutputAsync(
        string pipeName,
        string pattern,
        bool useRegex = false,
        CancellationToken ct = default)
    {
        try
        {
            var output = await ReadPipeOutputAsync(pipeName, ct);

            if (useRegex)
            {
                var regex = new Regex(pattern);
                return regex.IsMatch(output);
            }
            else
            {
                return output.Contains(pattern, StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching pipe {PipeName}", pipeName);
            return false;
        }
    }

    public Task ClearPipeAsync(string pipeName, CancellationToken ct = default)
    {
        var pipe = _pipeManager.GetPipe(pipeName);
        if (pipe is not null)
        {
            pipe.ClearBuffer();
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Cleared pipe {PipeName}", pipeName);
            }
        }

        return Task.CompletedTask;
    }
}
