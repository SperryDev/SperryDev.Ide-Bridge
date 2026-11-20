namespace TokenSaver.TerminalControl.Core;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Principal;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using TokenSaver.TerminalControl.Contracts;
using TokenSaver.TerminalControl.Models;
using TokenSaver.TerminalControl.Output;

public class StandardTerminalSession : ITerminalSession, IDisposable
{
    private static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromSeconds(60);
    // Long enough for a UAC approval before the elevated wrapper connects.
    private static readonly TimeSpan CommandConnectTimeout = TimeSpan.FromSeconds(60);

    private readonly ILogger _logger;
    private readonly NamedPipeManager _pipeManager;
    private readonly bool _ownsPipeManager;
    private readonly ITerminalProcessLauncher? _injectedLauncher;
    private ITerminalProcessLauncher _launcher;
    private SecurityIdentifier? _clientSid;
    private bool _headless;
    private readonly ConcurrentDictionary<string, TabRuntime> _tabs = new();
    private Process? _process;
    private TerminalSessionConfig? _config;
    private TerminalConfiguration _terminalConfig = null!;

    public string SessionId { get; } = $"standard_{Guid.NewGuid():N}".Substring(0, 20);
    private string WindowName => $"tokensaver-{SessionId}";
    private static string WrapperPath => Path.Combine(AppContext.BaseDirectory, "tokensaver-tab.ps1");
    public bool IsElevated { get; }
    public int ProcessId => _process?.Id ?? -1;
    public TerminalSessionState State { get; private set; } = TerminalSessionState.Initializing;
    public TerminalConfiguration Configuration => _terminalConfig;

    public StandardTerminalSession(
        ILogger logger,
        NamedPipeManager? pipeManager = null,
        ITerminalProcessLauncher? launcher = null)
        : this(logger, pipeManager, launcher, elevated: false)
    {
    }

    /// <param name="elevated">When true, the session window is opened with UAC (runas).
    /// Tabs are opened with the same command either way: a tab opened from a standard
    /// process into an elevated window runs elevated.</param>
    protected StandardTerminalSession(
        ILogger logger,
        NamedPipeManager? pipeManager,
        ITerminalProcessLauncher? launcher,
        bool elevated)
    {
        _logger = logger;
        IsElevated = elevated;
        _injectedLauncher = launcher;
        _launcher = launcher ?? new ProcessTerminalLauncher();
        // Create a logger for the pipe manager if not provided
        if (pipeManager is null)
        {
            using var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => builder.AddDebug());
            var pipeLogger = loggerFactory.CreateLogger<NamedPipeManager>();
            _pipeManager = new NamedPipeManager(pipeLogger);
            _ownsPipeManager = true;
        }
        else
        {
            _pipeManager = pipeManager;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        foreach (var tab in _tabs.Keys.ToList())
        {
            if (_tabs.TryRemove(tab, out var runtime))
            {
                runtime.Dispose();
            }
        }

        if (_ownsPipeManager)
        {
            _pipeManager.Dispose();
        }

        _process?.Dispose();
        _process = null;
    }

    public async Task InitializeAsync(TerminalSessionConfig config, CancellationToken ct = default)
    {
        _config = config;
        State = TerminalSessionState.Initializing;

        try
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Initializing standard terminal session {SessionId}", SessionId);
            }

            using var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => builder.AddDebug());
            var discoveryLogger = loggerFactory.CreateLogger<Discovery.TerminalConfigurationDiscovery>();
            var discovery = new Discovery.TerminalConfigurationDiscovery(discoveryLogger);
            _terminalConfig = await discovery.DiscoverConfigurationAsync(ct);

            // Headless: the wrapper runs under TokenSaver's own account with no window; only the pipes are used.
            _headless = !IsElevated && config.LaunchMode == TerminalLaunchMode.ServiceAccount;
            _clientSid = null;
            if (!_headless && _injectedLauncher is null && ConsoleUserSession.RunningAsLocalSystem())
            {
                // LocalSystem runs in session 0 and can't show a window: start the terminal in the logged-on
                // user's session and grant that user the pipes.
                _launcher = new ConsoleUserTerminalLauncher();
                _clientSid = ConsoleUserSession.GetConsoleUserSid();
            }

            // Open this session's own Windows Terminal window. Throws if wt.exe is missing.
            // An elevated session opens no window here: each tab prompts UAC when it is created.
            if (!IsElevated && !_headless)
            {
                _process = _launcher.Start(new ProcessStartInfo
                {
                    FileName = "wt.exe",
                    Arguments = $"-w {WindowName}",
                    UseShellExecute = false,
                });
            }

            State = TerminalSessionState.Ready;
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Standard terminal session {SessionId} initialized", SessionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize standard terminal session {SessionId}", SessionId);
            State = TerminalSessionState.Terminated;
            throw;
        }
    }

    public Task<TerminalTabInfo> CreateTabAsync(
        string tabName,
        string? initialCommand = null,
        CancellationToken ct = default)
    {
        EnsureReady();

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Creating tab '{TabName}' in standard session {SessionId}", tabName, SessionId);
        }

        var tabId = $"{SessionId}-{Guid.NewGuid():N}".Substring(0, 24);

        // Output and command pipes for this tab. Stderr is reported in-band on stdout.
        var stdoutPipe = _pipeManager.CreateOutputPipe(SessionId, tabId, NamedPipeStreamType.Stdout, _clientSid);
        var stderrPipe = _pipeManager.CreateOutputPipe(SessionId, tabId, NamedPipeStreamType.Stderr, _clientSid);
        var commandPipeName = _pipeManager.CreateCommandPipe(SessionId, tabId, _clientSid);

        var tabInfo = new TerminalTabInfo(
            TabId: tabId,
            TabName: tabName,
            SessionId: SessionId,
            CreatedAt: DateTime.UtcNow,
            IsActive: true,
            CurrentCommand: initialCommand,
            StdoutPipe: stdoutPipe,
            StderrPipe: stderrPipe);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Tab {TabId} created with pipes: stdout={StdoutPipe}, command={CommandPipe}",
                tabId,
                stdoutPipe.PipePath,
                commandPipeName);
        }

        // Open the tab in this session's window, running the output wrapper.
        try
        {
            _launcher.Start(new ProcessStartInfo
            {
                FileName = _headless ? "pwsh" : "wt.exe",
                Arguments = _headless
                    ? $"-NoProfile -File \"{WrapperPath}\" -StdoutPipe \"{stdoutPipe.PipeName}\" -CommandPipe \"{commandPipeName}\""
                    : $"-w {WindowName} new-tab --title \"{tabName}\" pwsh -NoProfile -File \"{WrapperPath}\" " +
                        $"-StdoutPipe \"{stdoutPipe.PipeName}\" -CommandPipe \"{commandPipeName}\"",
                // Elevated tabs: UAC prompt per tab. They join the same elevated window by name.
                Verb = IsElevated ? "runas" : null,
                UseShellExecute = IsElevated,
            });
        }
        catch
        {
            _pipeManager.ClosePipe(stdoutPipe.PipeName);
            _pipeManager.ClosePipe(stderrPipe.PipeName);
            _pipeManager.ClosePipe(commandPipeName);
            throw;
        }

        var runtime = new TabRuntime(tabInfo, commandPipeName);
        _tabs[tabId] = runtime;
        StartStdoutPump(stdoutPipe.PipeName, runtime.StdoutLines);

        return Task.FromResult(tabInfo);
    }

    public async Task<TerminalExecutionResult> ExecuteInTabAsync(
        string tabId,
        string command,
        string? workingDirectory = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        EnsureReady();

        if (!_tabs.TryGetValue(tabId, out var runtime))
        {
            throw new InvalidOperationException($"Tab '{tabId}' does not exist in session {SessionId}.");
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Executing command in tab {TabId}: {Command}", tabId, command);
        }

        var fullCommand = workingDirectory is null
            ? command
            : $"Set-Location -LiteralPath '{workingDirectory.Replace("'", "''", StringComparison.Ordinal)}'; {command}";
        var requestId = Guid.NewGuid().ToString("N")[..8];
        var line = TabProtocol.FormatCommand(requestId, fullCommand);

        var commandPipe = await EnsureCommandConnectedAsync(runtime, ct);
        await commandPipe.WriteLineAsync(line);
        runtime.Pending.Enqueue(requestId);

        var stopwatch = Stopwatch.StartNew();
        using var timeoutCts = new CancellationTokenSource(timeout ?? DefaultCommandTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        var collected = new List<string>();
        var errors = new List<string>();
        try
        {
            while (await runtime.StdoutLines.Reader.WaitToReadAsync(linked.Token))
            {
                while (runtime.StdoutLines.Reader.TryRead(out var outLine))
                {
                    if (TabProtocol.ParseEnd(outLine) is { } endLine)
                    {
                        var endId = endLine.RequestId;
                        var exitCode = endLine.ExitCode;
                        // Requests finish in order; only the head of the queue is ours to finish.
                        if (runtime.Pending.TryPeek(out var head) && head == endId)
                        {
                            runtime.Pending.TryDequeue(out _);
                        }

                        if (endId == requestId)
                        {
                            return new TerminalExecutionResult(
                                Success: exitCode == 0,
                                ExitCode: exitCode,
                                Stdout: string.Join("\n", collected),
                                Stderr: errors.Count == 0 ? null : string.Join("\n", errors),
                                ExecutionTimeMs: stopwatch.ElapsedMilliseconds);
                        }
                    }
                    else if (TabProtocol.ParseError(outLine) is { } errLine)
                    {
                        var errId = errLine.RequestId;
                        var errText = errLine.Text;
                        if (runtime.Pending.TryPeek(out var current) && current == requestId && errId == requestId)
                        {
                            errors.Add(errText);
                        }
                    }
                    else if (runtime.Pending.TryPeek(out var current) && current == requestId)
                    {
                        collected.Add(outLine);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Leave the request queued so its late output is discarded by the next call.
            return new TerminalExecutionResult(
                Success: false,
                ExitCode: -1,
                Stdout: string.Join("\n", collected),
                Stderr: errors.Count == 0 ? null : string.Join("\n", errors),
                ExecutionTimeMs: stopwatch.ElapsedMilliseconds,
                Errors: new[] { $"Command timed out after {stopwatch.Elapsed.TotalSeconds:F0}s." });
        }

        throw new InvalidOperationException($"Tab '{tabId}' closed before the command finished.");
    }

    /// <summary>
    /// Asks the tab's wrapper to exit. wt has no close-tab command, so the wt tab
    /// closes only when the wrapper process exits; this method does not force it closed.
    /// </summary>
    public async Task CloseTabAsync(string tabId, CancellationToken ct = default)
    {
        EnsureReady();
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Closing tab {TabId} in standard session {SessionId}", tabId, SessionId);
        }

        if (!_tabs.TryRemove(tabId, out var runtime))
        {
            return;
        }

        // Asking the wrapper to exit closes the wt tab; the wrapper then drops its pipes.
        if (runtime.CommandConnected)
        {
            try
            {
                var commandPipe = _pipeManager.GetCommandPipe(runtime.CommandPipeName);
                if (commandPipe is not null)
                {
                    await commandPipe.WriteLineAsync(TabProtocol.FormatCommand("close", TabProtocol.CloseCommand));
                }
            }
            catch (IOException ex)
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(ex, "Tab {TabId} command pipe already closed", tabId);
                }
            }
        }

        runtime.Dispose();
        if (runtime.Info.StdoutPipe is { } stdoutPipe)
        {
            _pipeManager.ClosePipe(stdoutPipe.PipeName);
        }

        if (runtime.Info.StderrPipe is { } stderrPipe)
        {
            _pipeManager.ClosePipe(stderrPipe.PipeName);
        }

        _pipeManager.ClosePipe(runtime.CommandPipeName);
    }

    public async Task TerminateAsync(bool graceful = true, CancellationToken ct = default)
    {
        if (State == TerminalSessionState.Terminated || State == TerminalSessionState.Closing)
        {
            return;
        }

        State = TerminalSessionState.Closing;
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Terminating standard session {SessionId} (Graceful: {Graceful})", SessionId, graceful);
        }

        try
        {
            foreach (var tabId in _tabs.Keys.ToList())
            {
                await CloseTabAsync(tabId, ct);
            }

            if (_process is not null && !_process.HasExited)
            {
                if (graceful)
                {
                    _process.CloseMainWindow();
                    if (!await WaitForExitAsync(_process, _config?.ActualGracefulCloseTimeout ?? TimeSpan.FromSeconds(5)))
                    {
                        _process.Kill();
                    }
                }
                else
                {
                    _process.Kill();
                }

                _process.Dispose();
            }

            State = TerminalSessionState.Terminated;
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Standard session {SessionId} terminated", SessionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error terminating standard session {SessionId}", SessionId);
            State = TerminalSessionState.Terminated;
        }
        finally
        {
            // Clean up all pipes for this session
            _pipeManager.CloseSessionPipes(SessionId);
        }
    }

    public async IAsyncEnumerable<TerminalTabInfo> ListTabsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        EnsureReady();
        await Task.CompletedTask;
        foreach (var runtime in _tabs.Values)
        {
            ct.ThrowIfCancellationRequested();
            yield return runtime.Info;
        }
    }

    public Task<TerminalTabInfo?> GetTabAsync(string tabId, CancellationToken ct = default)
    {
        EnsureReady();
        return Task.FromResult(_tabs.TryGetValue(tabId, out var runtime) ? runtime.Info : null);
    }

    public Task<bool> IsAliveAsync(CancellationToken ct = default)
    {
        return Task.FromResult(State == TerminalSessionState.Ready);
    }

    private void EnsureReady()
    {
        if (State != TerminalSessionState.Ready)
        {
            throw new InvalidOperationException($"Session is not in Ready state. Current state: {State}");
        }
    }

    /// <summary>Waits for the wrapper to connect to the command pipe, once per tab.</summary>
    private async Task<NamedCommandPipe> EnsureCommandConnectedAsync(TabRuntime runtime, CancellationToken ct)
    {
        var commandPipe = _pipeManager.GetCommandPipe(runtime.CommandPipeName)
            ?? throw new InvalidOperationException($"Command pipe {runtime.CommandPipeName} is not open.");

        if (!runtime.CommandConnected)
        {
            await commandPipe.WaitForClientAsync(CommandConnectTimeout, ct);
            runtime.CommandConnected = true;
        }

        return commandPipe;
    }

    private void StartStdoutPump(string pipeName, Channel<string> target)
    {
        var pipe = _pipeManager.GetPipe(pipeName);
        if (pipe is null)
        {
            target.Writer.TryComplete();
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var line in pipe.ReadLinesAsync())
                {
                    target.Writer.TryWrite(line);
                }
            }
            catch (Exception ex)
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(ex, "Output pump for {PipeName} stopped", pipeName);
                }
            }
            finally
            {
                target.Writer.TryComplete();
            }
        });
    }

    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout)
    {
        try
        {
            return await Task.Run(() => process.WaitForExit((int)timeout.TotalMilliseconds));
        }
        catch
        {
            return false;
        }
    }

    private sealed class TabRuntime : IDisposable
    {
        public TabRuntime(TerminalTabInfo info, string commandPipeName)
        {
            Info = info;
            CommandPipeName = commandPipeName;
        }

        public TerminalTabInfo Info { get; }
        public string CommandPipeName { get; }
        public Channel<string> StdoutLines { get; } = Channel.CreateUnbounded<string>();
        public Queue<string> Pending { get; } = new();
        public bool CommandConnected { get; set; }

        public void Dispose()
        {
            // Pipes are owned by NamedPipeManager and closed by the caller.
            StdoutLines.Writer.TryComplete();
        }
    }
}
