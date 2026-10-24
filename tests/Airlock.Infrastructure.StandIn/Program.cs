// Plays Syft for the Infrastructure's process tests (U3-NFR-04, U3-NFR-12). The first argument is the mode, Syft's own
// command where it has one; the STANDIN_* variables, which the tests set for each run, script what it does.
using System.Collections;
using System.Diagnostics;
using System.Globalization;

var mode = args.Length > 0 ? args[0] : string.Empty;
return mode switch
{
    "scan" => await ScanAsync(args),
    "version" => Version(),
    "hang" => Hang(),
    "tree" => Tree(),
    "args" => Arguments(args),
    "env" => Environment(),
    "flood" => await FloodAsync(),
    _ => Unknown(),
};

// `syft scan <source> -o json -q`: writes its output — STANDIN_OUTPUT's file, else "stand-in-output.json" inside a dir:
// source or "<file>.stand-in-output.json" next to a file source — to standard output, STANDIN_STDERR (or STANDIN_STDERR_KB
// kilobytes) to standard error, its arguments to STANDIN_ARGS_FILE, and a read-only file to its temporary folder; then it
// hangs if STANDIN_HANG is set, and exits with STANDIN_EXIT.
static async Task<int> ScanAsync(string[] args)
{
    if (System.Environment.GetEnvironmentVariable("STANDIN_ARGS_FILE") is { Length: > 0 } argsFile)
    {
        await File.WriteAllLinesAsync(argsFile, args);
    }

    if (System.Environment.GetEnvironmentVariable("TMPDIR") is { Length: > 0 } temporary && Directory.Exists(temporary))
    {
        var leftover = Path.Combine(temporary, "stand-in.tmp");
        await File.WriteAllTextAsync(leftover, "a temporary file Syft would leave behind");
        File.SetAttributes(leftover, FileAttributes.ReadOnly);
    }

    var source = args.Length > 1 ? args[1] : string.Empty;
    var output = System.Environment.GetEnvironmentVariable("STANDIN_OUTPUT") is { Length: > 0 } scripted
        ? scripted
        : source.StartsWith("dir:", StringComparison.Ordinal)
            ? Path.Combine(source[4..], "stand-in-output.json")
            : source + ".stand-in-output.json";
    if (File.Exists(output))
    {
        await using var file = File.OpenRead(output);
        await using var stdout = Console.OpenStandardOutput();
        await file.CopyToAsync(stdout);
    }

    WriteErrors();
    if (System.Environment.GetEnvironmentVariable("STANDIN_HANG") is { Length: > 0 })
    {
        return Hang();
    }

    return ExitCode();
}

static int Version()
{
    if (System.Environment.GetEnvironmentVariable("STANDIN_HANG") is { Length: > 0 })
    {
        return Hang();
    }

    Console.Out.Write(System.Environment.GetEnvironmentVariable("STANDIN_VERSION_TEXT")
        ?? "Application:   syft\nVersion:       1.55.0\nPlatform:      stand-in\n");
    WriteErrors();
    return ExitCode();
}

// Prints every argument after the mode on a line of its own.
static int Arguments(string[] args)
{
    foreach (var argument in args.Skip(1))
    {
        Console.Out.WriteLine(argument);
    }

    return 0;
}

static int Hang()
{
    Thread.Sleep(Timeout.Infinite);
    return 0;
}

// Starts a child of itself that hangs, prints its own and the child's process ids, then hangs too.
static int Tree()
{
    using var child = Process.Start(new ProcessStartInfo(System.Environment.ProcessPath!, "hang") { UseShellExecute = false })!;
    Console.Out.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{System.Environment.ProcessId} {child.Id}"));
    Console.Out.Flush();
    return Hang();
}

static int Environment()
{
    foreach (DictionaryEntry variable in System.Environment.GetEnvironmentVariables())
    {
        Console.Out.WriteLine($"{variable.Key}={variable.Value}");
    }

    return 0;
}

// Writes STANDIN_FLOOD_MB megabytes to standard output, more than any pipe holds.
static async Task<int> FloodAsync()
{
    var megabytes = int.Parse(System.Environment.GetEnvironmentVariable("STANDIN_FLOOD_MB") ?? "16", CultureInfo.InvariantCulture);
    var block = new byte[1024 * 1024];
    Array.Fill(block, (byte)'x');
    await using var stdout = Console.OpenStandardOutput();
    for (var i = 0; i < megabytes; i++)
    {
        await stdout.WriteAsync(block);
    }

    return 0;
}

static int Unknown()
{
    Console.Error.WriteLine("stand-in: unknown mode");
    return 64;
}

static void WriteErrors()
{
    if (System.Environment.GetEnvironmentVariable("STANDIN_STDERR") is { Length: > 0 } text)
    {
        Console.Error.Write(text);
    }

    if (System.Environment.GetEnvironmentVariable("STANDIN_STDERR_KB") is { Length: > 0 } kilobytes)
    {
        var line = new string('e', 1023);
        for (var i = 0; i < int.Parse(kilobytes, CultureInfo.InvariantCulture); i++)
        {
            Console.Error.WriteLine(line);
        }

        Console.Error.Write("END-OF-ERRORS");
    }

    Console.Error.Flush();
}

static int ExitCode() =>
    int.Parse(System.Environment.GetEnvironmentVariable("STANDIN_EXIT") ?? "0", CultureInfo.InvariantCulture);
