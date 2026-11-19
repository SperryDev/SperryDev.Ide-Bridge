namespace TokenSaver.TerminalControl.Contracts;

using TokenSaver.TerminalControl.Models;

/// <summary>
/// Monitors output via named pipes for tabs and sessions.
/// </summary>
public interface INamedPipeMonitor
{
    /// <summary>
    /// Monitors a pipe in real-time, yielding lines as they arrive.
    /// </summary>
    IAsyncEnumerable<string> MonitorPipeAsync(
        string pipeName,
        CancellationToken ct = default);

    /// <summary>
    /// Reads all accumulated output from a pipe.
    /// </summary>
    Task<string> ReadPipeOutputAsync(string pipeName, CancellationToken ct = default);

    /// <summary>
    /// Gets the current size of accumulated data in a pipe.
    /// </summary>
    Task<long> GetPipeSizeAsync(string pipeName, CancellationToken ct = default);

    /// <summary>
    /// Monitors multiple pipes simultaneously and yields output from any pipe.
    /// </summary>
    IAsyncEnumerable<(string PipeName, string Output)> MonitorMultiplePipesAsync(
        IEnumerable<string> pipeNames,
        CancellationToken ct = default);

    /// <summary>
    /// Searches for a pattern in pipe output without blocking the pipe.
    /// </summary>
    Task<bool> SearchPipeOutputAsync(
        string pipeName,
        string pattern,
        bool useRegex = false,
        CancellationToken ct = default);

    /// <summary>
    /// Clears accumulated output from a pipe.
    /// </summary>
    Task ClearPipeAsync(string pipeName, CancellationToken ct = default);
}
