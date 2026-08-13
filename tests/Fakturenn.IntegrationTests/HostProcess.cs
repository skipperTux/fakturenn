using System.Diagnostics;
using System.Runtime.CompilerServices;
using AwesomeAssertions;

namespace Fakturenn.IntegrationTests;

/// <summary>
/// Runs the built host assembly as a real subprocess.
/// <para>
/// <c>Program.cs</c> is top-level statements, so <b>anything wired there is unreachable
/// from an in-process test.</b> A test over the command class alone stays green when its
/// call site is deleted, and the call site is exactly the part that silently disappears.
/// Running <c>dotnet Fakturenn.Web.dll --something</c> and asserting on the exit code and
/// the effect is the only way to cover the dispatch.
/// </para>
/// </summary>
internal static class HostProcess
{
    /// <summary>
    /// Exit code reported when the process had to be killed. A host that keeps running is
    /// the observable shape of a missing dispatch: <c>TryRunAsync</c> answered null and
    /// the process fell through to serving traffic instead of exiting.
    /// </summary>
    internal const int TimedOutExitCode = -1;

    private static readonly TimeSpan _timeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Starts the host with <paramref name="arguments"/>, feeds
    /// <paramref name="standardInput"/> in, and returns its exit code with standard
    /// output and standard error concatenated.
    /// </summary>
    internal static async Task<(int ExitCode, string Output)> RunAsync(
        string connectionString,
        IEnumerable<string> arguments,
        string? standardInput,
        CancellationToken cancellationToken)
    {
        string hostAssembly = HostAssemblyPath();
        File.Exists(hostAssembly).Should().BeTrue($"the host must be built at {hostAssembly}");

        ProcessStartInfo startInfo = new("dotnet")
        {
            ArgumentList = { hostAssembly },
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["ConnectionStrings__Fakturenn"] = connectionString;

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the host process.");

        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> standardError = process.StandardError.ReadToEndAsync(cancellationToken);

        if (standardInput is not null)
        {
            await process.StandardInput.WriteLineAsync(standardInput);
        }

        // Always closed, including when nothing was written: a command reading a password
        // from a pipe that is never closed would block until the timeout below, and
        // "no password on standard input" is a case these tests assert on.
        process.StandardInput.Close();

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);

            return (
                TimedOutExitCode,
                $"The host did not exit within {_timeout}. It was serving traffic instead, which is what "
                + $"a missing dispatch looks like.{Environment.NewLine}{await standardOutput}{await standardError}");
        }

        return (process.ExitCode, await standardOutput + await standardError);
    }

    private static string RepositoryRoot([CallerFilePath] string sourceFilePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFilePath)!, "..", ".."));

    /// <summary>
    /// The host assembly built for the same configuration as this test assembly.
    /// Building this project builds <c>Fakturenn.Web</c> into its own <c>bin</c>, next to
    /// the <c>runtimeconfig.json</c> and <c>appsettings.json</c> the entrypoint needs, so
    /// no separate build step is required.
    /// </summary>
    private static string HostAssemblyPath()
    {
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;

        return Path.Combine(
            RepositoryRoot(), "src", "Fakturenn.Web", "bin", configuration, "net10.0", "Fakturenn.Web.dll");
    }
}
