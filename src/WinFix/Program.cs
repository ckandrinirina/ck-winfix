using WinFix.Pipeline;
using WinFix.System;

namespace WinFix;

public static class Program
{
    public static Task<int> Main(string[] args) => RunAsync(args, Console.Out);

    public static async Task<int> RunAsync(string[] args, TextWriter output)
    {
        if (!args.Contains("--dry-run"))
        {
            output.WriteLine("Only --dry-run is available yet.");
            return 2;
        }

        return await new FixPipeline(new FakeSystem(), output).RunAsync(CancellationToken.None);
    }
}
