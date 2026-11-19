namespace TokenSaver.LogHelper.Contracts;

/// <summary>Runs a program and returns its standard output. Arguments are passed as a list, never as a shell string.</summary>
public interface IProcessRunner
{
    Task<string> RunAsync(string file, IReadOnlyList<string> args, CancellationToken ct);
}
