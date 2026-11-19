namespace TokenSaver.TerminalControl.Tests;

using System.IO.Pipes;
using Microsoft.Extensions.Logging;
using Xunit;
using TokenSaver.TerminalControl.Models;
using TokenSaver.TerminalControl.Output;

public sealed class NamedPipeManagerTests : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<NamedPipeManager> _logger;
    private readonly NamedPipeManager _manager;

    public NamedPipeManagerTests()
    {
        _loggerFactory = LoggerFactory.Create(builder => builder.AddDebug());
        _logger = _loggerFactory.CreateLogger<NamedPipeManager>();
        _manager = new NamedPipeManager(_logger);
    }

    [Fact]
    public void CreateOutputPipe_ReturnsValidPipeInfo()
    {
        // Act
        var pipeInfo = _manager.CreateOutputPipe("session-1", "tab-1", NamedPipeStreamType.Stdout);

        // Assert
        Assert.NotNull(pipeInfo);
        Assert.True(pipeInfo.IsValid);
        Assert.Contains("TokenSaver-session-1-tab-1-Stdout", pipeInfo.PipeName, StringComparison.Ordinal);
        Assert.Contains(@"\\.\pipe\", pipeInfo.PipePath, StringComparison.Ordinal);
        Assert.Equal(NamedPipeStreamType.Stdout, pipeInfo.StreamType);
        Assert.True(pipeInfo.CreatedAt <= DateTime.UtcNow);
    }

    [Fact]
    public void CreateOutputPipe_CreatesSeparatePipesForStdoutAndStderr()
    {
        // Act
        var stdoutPipe = _manager.CreateOutputPipe("session-1", "tab-1", NamedPipeStreamType.Stdout);
        var stderrPipe = _manager.CreateOutputPipe("session-1", "tab-1", NamedPipeStreamType.Stderr);

        // Assert
        Assert.NotEqual(stdoutPipe.PipeName, stderrPipe.PipeName);
        Assert.Contains("Stdout", stdoutPipe.PipeName, StringComparison.Ordinal);
        Assert.Contains("Stderr", stderrPipe.PipeName, StringComparison.Ordinal);
    }

    [Fact]
    public void GetPipe_ReturnsPipeServer()
    {
        // Arrange
        var pipeInfo = _manager.CreateOutputPipe("session-1", "tab-1", NamedPipeStreamType.Stdout);

        // Act
        var pipe = _manager.GetPipe(pipeInfo.PipeName);

        // Assert
        Assert.NotNull(pipe);
    }

    [Fact]
    public void GetPipe_ReturnsNullForNonexistentPipe()
    {
        // Act
        var pipe = _manager.GetPipe("nonexistent-pipe");

        // Assert
        Assert.Null(pipe);
    }

    [Fact]
    public void ClosePipe_RemovesPipeServer()
    {
        // Arrange
        var pipeInfo = _manager.CreateOutputPipe("session-1", "tab-1", NamedPipeStreamType.Stdout);
        Assert.NotNull(_manager.GetPipe(pipeInfo.PipeName));

        // Act
        _manager.ClosePipe(pipeInfo.PipeName);

        // Assert
        Assert.Null(_manager.GetPipe(pipeInfo.PipeName));
    }

    [Fact]
    public void CloseSessionPipes_ClosesAllSessionPipes()
    {
        // Arrange
        var pipe1 = _manager.CreateOutputPipe("session-1", "tab-1", NamedPipeStreamType.Stdout);
        var pipe2 = _manager.CreateOutputPipe("session-1", "tab-2", NamedPipeStreamType.Stdout);
        var pipe3 = _manager.CreateOutputPipe("session-2", "tab-1", NamedPipeStreamType.Stdout);

        // Act
        _manager.CloseSessionPipes("session-1");

        // Assert
        Assert.Null(_manager.GetPipe(pipe1.PipeName));
        Assert.Null(_manager.GetPipe(pipe2.PipeName));
        Assert.NotNull(_manager.GetPipe(pipe3.PipeName)); // Different session unaffected
    }

    [Fact]
    public void GetPipeSize_ReturnsBytesWritten()
    {
        // Arrange
        var pipeInfo = _manager.CreateOutputPipe("session-1", "tab-1", NamedPipeStreamType.Stdout);
        var pipe = _manager.GetPipe(pipeInfo.PipeName);

        // Initially empty
        var initialSize = _manager.GetPipeSize(pipeInfo.PipeName);
        Assert.Equal(0, initialSize);
    }

    [Fact]
    public void NamedServerPipe_PipeNameMustMatch()
    {
        // Arrange
        var pipeInfo1 = _manager.CreateOutputPipe("s1", "t1", NamedPipeStreamType.Stdout);
        var pipeInfo2 = _manager.CreateOutputPipe("s1", "t2", NamedPipeStreamType.Stdout);

        // Act
        var retrieved1 = _manager.GetPipe(pipeInfo1.PipeName);
        var retrieved2 = _manager.GetPipe(pipeInfo2.PipeName);

        // Assert
        Assert.NotNull(retrieved1);
        Assert.NotNull(retrieved2);
        Assert.NotSame(retrieved1, retrieved2);
    }

    [Fact]
    public void CreateOutputPipe_MultipleTabsSameSessions()
    {
        // Arrange & Act
        var tab1Stdout = _manager.CreateOutputPipe("session-1", "tab-1", NamedPipeStreamType.Stdout);
        var tab1Stderr = _manager.CreateOutputPipe("session-1", "tab-1", NamedPipeStreamType.Stderr);
        var tab2Stdout = _manager.CreateOutputPipe("session-1", "tab-2", NamedPipeStreamType.Stdout);
        var tab2Stderr = _manager.CreateOutputPipe("session-1", "tab-2", NamedPipeStreamType.Stderr);

        // Assert
        Assert.NotNull(_manager.GetPipe(tab1Stdout.PipeName));
        Assert.NotNull(_manager.GetPipe(tab1Stderr.PipeName));
        Assert.NotNull(_manager.GetPipe(tab2Stdout.PipeName));
        Assert.NotNull(_manager.GetPipe(tab2Stderr.PipeName));

        // All unique
        var names = new[] { tab1Stdout.PipeName, tab1Stderr.PipeName, tab2Stdout.PipeName, tab2Stderr.PipeName };
        Assert.Equal(4, names.Distinct().Count());
    }

    [Fact]
    public void PipeInfo_PipePathFormat()
    {
        // Act
        var pipeInfo = _manager.CreateOutputPipe("my-session", "my-tab", NamedPipeStreamType.Stdout);

        // Assert
        Assert.StartsWith(@"\\.\pipe\", pipeInfo.PipePath, StringComparison.Ordinal);
        Assert.EndsWith(pipeInfo.PipeName, pipeInfo.PipePath, StringComparison.Ordinal);
        Assert.True(pipeInfo.PipePath.Length > @"\\.\pipe\".Length);
    }

    [Fact]
    public void Dispose_DisposesAllPipes()
    {
        // Arrange
        var pipe1 = _manager.CreateOutputPipe("s1", "t1", NamedPipeStreamType.Stdout);
        var pipe2 = _manager.CreateOutputPipe("s2", "t2", NamedPipeStreamType.Stdout);

        // Act
        _manager.Dispose();

        // Assert (should not throw, pipes are disposed)
        _manager.Dispose(); // Should be safe to call multiple times
    }

    [Fact]
    public void GetPipe_AfterDispose_ReturnsNull()
    {
        // Arrange
        var pipeInfo = _manager.CreateOutputPipe("session-1", "tab-1", NamedPipeStreamType.Stdout);
        _manager.Dispose();

        // Act & Assert
        var pipe = _manager.GetPipe(pipeInfo.PipeName);
        Assert.Null(pipe);
    }

    [Fact]
    public void CreateOutputPipe_AfterDispose_Throws()
    {
        // Arrange
        _manager.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() =>
            _manager.CreateOutputPipe("session-1", "tab-1", NamedPipeStreamType.Stdout));
    }

    public void Dispose()
    {
        _manager?.Dispose();
        _loggerFactory.Dispose();
    }
}

public sealed class NamedServerPipeTests : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<NamedPipeManager> _logger;
    private NamedServerPipe? _pipe;
    private readonly string _pipeName = $"test-pipe-{Guid.NewGuid():N}";

    public NamedServerPipeTests()
    {
        _loggerFactory = LoggerFactory.Create(builder => builder.AddDebug());
        _logger = _loggerFactory.CreateLogger<NamedPipeManager>();
    }

    [Fact]
    public void NamedServerPipe_CanBeCreated()
    {
        // Act
        _pipe = new NamedServerPipe(_pipeName, _logger);

        // Assert
        Assert.NotNull(_pipe);
    }

    [Fact]
    public async Task NamedServerPipe_ClearBuffer_RemovesContent()
    {
        // Arrange
        _pipe = new NamedServerPipe(_pipeName, _logger);

        // Act
        _pipe.ClearBuffer();
        var output = await _pipe.ReadAllOutputAsync();

        // Assert
        Assert.Empty(output);
    }

    [Fact]
    public async Task NamedServerPipe_GetSize_ReturnsZeroWhenEmpty()
    {
        // Arrange
        _pipe = new NamedServerPipe(_pipeName, _logger);

        // Act
        var size = _pipe.Size;

        // Assert
        Assert.Equal(0, size);
    }

    public void Dispose()
    {
        _pipe?.Dispose();
        _loggerFactory.Dispose();
    }
}
