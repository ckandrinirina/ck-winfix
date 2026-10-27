using WinFix.Media;
using WinFix.Pipeline;
using WinFix.Problems;
using WinFix.Reset;
using WinFix.System;

namespace WinFix;

public static class Program
{
    public static Task<int> Main(string[] args) => RunAsync(args, Console.Out);

    /// <summary>Every problem module, cheapest and least invasive first.</summary>
    public static IReadOnlyList<IProblemModule> DefaultModules() =>
    [
        new ShellProblem(),
        new WindowsUpdateProblem(),
        new BlueScreenProblem(),
        new DiskProblem(),
        new StartAppsProblem(),
        new ProfileProblem(),
        new NetworkProblem(),
    ];

    /// <summary>--dry-run never touches the machine.</summary>
    public static ISystem CreateSystem(bool dryRun) => dryRun ? new FakeSystem() : new WindowsSystem();

    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        Func<bool>? isAdmin = null,
        ISystem? system = null,
        Func<string?>? readLine = null)
    {
        var dryRun = args.Contains("--dry-run");
        if (!dryRun && !new AdminCheck(isAdmin ?? AdminCheck.IsElevatedOnWindows).TryProceed(output, out var exitCode))
        {
            return exitCode;
        }

        var onlyIndex = Array.IndexOf(args, "--only");
        if (onlyIndex >= 0 && onlyIndex + 1 >= args.Length)
        {
            output.WriteLine("--only needs a module name, for example: --only shell");
            return 2;
        }

        var only = onlyIndex >= 0 ? args[onlyIndex + 1] : null;
        system ??= CreateSystem(dryRun);
        using var http = dryRun ? null : new HttpClient();
        var resetChain = http is null ? null : RealResetChain(system, http, output);
        var pipeline = new FixPipeline(system, output, readLine: readLine, dryRun: dryRun, modules: DefaultModules(), only: only, resetChain: resetChain);
        return await pipeline.RunAsync(CancellationToken.None, args.Contains("--resume"));
    }

    private static ResetChain RealResetChain(ISystem system, HttpClient http, TextWriter output)
    {
        var downloader = new IsoDownloader(new MicrosoftIsoSource(http, system.Log), http, new DiskDownloadFiles(), output);
        return new ResetChain(ResetChain.DefaultAttempts(new MediaFinder().FindAsync, downloader.DownloadAsync, output));
    }
}
