namespace TokenSaver.TerminalControl.Models;

public sealed record NamedPipeInfo(
    string PipeName,
    string PipePath,
    NamedPipeStreamType StreamType,
    DateTime CreatedAt,
    long? BytesWritten = 0)
{
    public bool IsValid => !string.IsNullOrEmpty(PipeName) && !string.IsNullOrEmpty(PipePath);
}

public enum NamedPipeStreamType
{
    Stdout,
    Stderr,
    Combined
}
