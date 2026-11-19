namespace TokenSaver.TerminalControl.Contracts;

using TokenSaver.TerminalControl.Models;

public interface ITerminalOutputCapture
{
    /// <summary>
    /// Monitors tab output in real-time via an async enumerable.
    /// </summary>
    IAsyncEnumerable<TerminalOutputLine> MonitorTabOutputAsync(
        string tabId,
        CancellationToken ct = default);

    /// <summary>
    /// Gets captured output for a tab up to the specified max lines.
    /// </summary>
    Task<TerminalOutput> GetTabOutputAsync(
        string tabId,
        int? maxLines = 1000,
        CancellationToken ct = default);

    /// <summary>
    /// Gets error lines (stderr) from a tab's captured output.
    /// </summary>
    Task<IReadOnlyList<TerminalOutputLine>> GetErrorsAsync(
        string tabId,
        CancellationToken ct = default);

    /// <summary>
    /// Clears captured output for a tab.
    /// </summary>
    Task ClearTabOutputAsync(string tabId, CancellationToken ct = default);

    /// <summary>
    /// Searches output lines by pattern (substring or regex).
    /// </summary>
    Task<IReadOnlyList<TerminalOutputLine>> SearchOutputAsync(
        string tabId,
        string pattern,
        bool useRegex = false,
        CancellationToken ct = default);
}
