using WinFix.Pipeline;
using WinFix.System;

namespace WinFix;

public static class Program
{
    public static Task<int> Main(string[] args) => RunAsync(args, Console.Out);

    public static async Task<int> RunAsync(string[] args, TextWriter output, Func<bool>? isAdmin = null, ISystem? system = null)
    {
        if (!args.Contains("--dry-run"))
        {
            if (!new AdminCheck(isAdmin ?? AdminCheck.IsElevatedOnWindows).TryProceed(output, out var exitCode))
            {
                return exitCode;
            }

            output.WriteLine("Only --dry-run is available yet.");
            return 2;
        }

        var onlyIndex = Array.IndexOf(args, "--only");
        if (onlyIndex >= 0 && onlyIndex + 1 >= args.Length)
        {
            output.WriteLine("--only needs a module name, for example: --only shell");
            return 2;
        }

        var only = onlyIndex >= 0 ? args[onlyIndex + 1] : null;
        return await new FixPipeline(system ?? new FakeSystem(), output, dryRun: true, only: only).RunAsync(CancellationToken.None, args.Contains("--resume"));
    }
}
