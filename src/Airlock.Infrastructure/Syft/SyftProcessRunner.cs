using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Airlock.Infrastructure.Syft;

/// <summary>
/// Runs the Syft binary from start to exit (IR-01, IR-02, U3-NFR-04): no shell, the arguments as a list, standard output
/// handed to the caller's reader as a stream while the end of standard error is kept, and the whole process tree killed
/// the moment the scan is cancelled or the run's limit passes.
/// </summary>
internal sealed class SyftProcessRunner(TimeProvider timeProvider)
{
    /// <summary>How much of the end of standard error is kept (U3-NFR-04).</summary>
    public const int ErrorTailLength = 4 * 1024;

    /// <summary>How long a killed tree, or a pipe after the exit, is waited for.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    public async Task<SyftRunResult<T>> RunAsync<T>(
        SyftRunRequest request, Func<Stream, CancellationToken, Task<T>> readOutput, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(readOutput);
        cancellationToken.ThrowIfCancellationRequested();

        using var process = new Process { StartInfo = StartInfo(request) };
        try
        {
            process.Start();
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return new SyftRunResult<T>(SyftRunEnd.NotStarted, -1, default, string.Empty, exception.Message);
        }

        // IR-01: Syft never waits for input.
        process.StandardInput.Close();
        var errors = new ErrorTail(process.StandardError);
        using var limit = new CancellationTokenSource(request.TimeLimit, timeProvider);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, limit.Token);

        // U3-NFR-04: the tree is killed as soon as either fires, whatever is being read at that moment: reads from a
        // child's pipes cannot be interrupted, but they end once the process is gone.
        using (stop.Token.Register(static state => Kill((Process)state!), process))
        {
            var work = RunToEndAsync(process, readOutput, stop.Token);
            try
            {
                var output = await work.WaitAsync(stop.Token);
                await errors.FinishAsync(timeProvider);
                return new SyftRunResult<T>(SyftRunEnd.Exited, process.ExitCode, output, errors.Text, null);
            }
            catch (Exception) when (stop.IsCancellationRequested)
            {
                Observe(work);
            }
            catch
            {
                Kill(process);
                Observe(work);
                throw;
            }
        }

        Kill(process);
        await WaitForExitAsync(process);
        await errors.FinishAsync(timeProvider);
        cancellationToken.ThrowIfCancellationRequested();
        return new SyftRunResult<T>(SyftRunEnd.TimedOut, -1, default, errors.Text, null);
    }

    private static ProcessStartInfo StartInfo(SyftRunRequest request)
    {
        var info = new ProcessStartInfo(request.FileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in request.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        // U3-NFR-07: the NVD key never reaches Syft, whatever the variable holding it is called.
        foreach (var name in info.Environment.Keys.ToList())
        {
            var value = info.Environment[name];
            if (request.HiddenNames.Contains(name, StringComparer.OrdinalIgnoreCase)
                || (value is not null && request.HiddenValues.Contains(value, StringComparer.Ordinal)))
            {
                info.Environment.Remove(name);
            }
        }

        foreach (var (name, value) in request.Environment)
        {
            info.Environment[name] = value;
        }

        return info;
    }

    private static async Task<T> RunToEndAsync<T>(
        Process process, Func<Stream, CancellationToken, Task<T>> readOutput, CancellationToken cancellationToken)
    {
        var output = await readOutput(process.StandardOutput.BaseStream, cancellationToken);

        // Whatever the reader left is drained, so Syft never blocks on a full pipe; the exit code then decides (IR-02).
        await process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return output;
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            // It has exited already, or the system refused; the bounded wait that follows moves on either way.
        }
    }

    private static void Observe(Task task) =>
        task.ContinueWith(
            static finished => _ = finished.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private async Task WaitForExitAsync(Process process)
    {
        using var patience = new CancellationTokenSource(Patience, timeProvider);
        try
        {
            await process.WaitForExitAsync(patience.Token);
        }
        catch (OperationCanceledException)
        {
            // Reported by the caller's outcome; a tree that will not die must not hold the scan.
        }
    }

    /// <summary>Reads standard error to its end and keeps its last <see cref="ErrorTailLength"/> characters.</summary>
    private sealed class ErrorTail
    {
        private readonly Lock gate = new();
        private readonly StringBuilder text = new();
        private readonly Task reading;

        public ErrorTail(StreamReader reader) => reading = ReadAsync(reader);

        public string Text
        {
            get
            {
                lock (gate)
                {
                    return text.Length > ErrorTailLength
                        ? text.ToString(text.Length - ErrorTailLength, ErrorTailLength)
                        : text.ToString();
                }
            }
        }

        public async Task FinishAsync(TimeProvider timeProvider)
        {
            try
            {
                await reading.WaitAsync(Patience, timeProvider);
            }
            catch (TimeoutException)
            {
                // Something still holds the pipe; what has arrived is enough.
            }
        }

        private async Task ReadAsync(StreamReader reader)
        {
            var buffer = new char[4096];
            try
            {
                int read;
                while ((read = await reader.ReadAsync(buffer, CancellationToken.None)) > 0)
                {
                    lock (gate)
                    {
                        text.Append(buffer, 0, read);
                        if (text.Length > 2 * ErrorTailLength)
                        {
                            text.Remove(0, text.Length - ErrorTailLength);
                        }
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                // The pipe broke when the process was killed.
            }
        }
    }
}

/// <summary>One run of the Syft binary: its arguments, its time limit and the changes to its environment.</summary>
internal sealed record SyftRunRequest(string FileName, IReadOnlyList<string> Arguments, TimeSpan TimeLimit)
{
    /// <summary>Variables set for the run, such as the update check and the temporary folder.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();

    /// <summary>Variables removed by name, ignoring case.</summary>
    public IReadOnlyCollection<string> HiddenNames { get; init; } = [];

    /// <summary>Variables removed by value: the NVD key, whatever the variable is called.</summary>
    public IReadOnlyCollection<string> HiddenValues { get; init; } = [];
}

/// <summary>How a run ended.</summary>
internal enum SyftRunEnd
{
    Exited,
    TimedOut,
    NotStarted,
}

/// <summary>A run's end, exit code, what the reader made of standard output, and the end of standard error.</summary>
internal sealed record SyftRunResult<T>(SyftRunEnd End, int ExitCode, T? Output, string ErrorTail, string? StartFailure);
