using WinFix.System;

namespace WinFix.Repairs;

/// <summary>Resets the Windows Update components: stop services, clear the BITS queue, move the caches aside, restore service defaults, start again.</summary>
public sealed class ResetUpdateComponentsStep : IRepairStep
{
    private const string BitsQueueFiles = @"%ALLUSERSPROFILE%\Microsoft\Network\Downloader\qmgr*.dat";

    // Stop order and start order are reversed: cryptsvc is the dependency.
    private static readonly string[] StopOrder = ["bits", "wuauserv", "cryptsvc"];
    private static readonly string[] StartOrder = ["cryptsvc", "bits", "wuauserv"];

    // Cache folder under the Windows folder, and its bare name for ren.
    private static readonly (string Parent, string Name)[] Caches =
        [("SoftwareDistribution", "DataStore"), ("SoftwareDistribution", "Download"), ("System32", "catroot2")];

    private static readonly (string Service, string StartType)[] Defaults =
        [("wuauserv", "demand"), ("bits", "delayed-auto"), ("cryptsvc", "auto")];

    public string Name => "Reset Windows Update components";

    public async Task<StepResult> RunAsync(ISystem system, CancellationToken ct)
    {
        if (!CommandArgs.CanQuoteCmd(system.WindowsDirectory))
        {
            system.Log($"The Windows folder {system.WindowsDirectory} cannot be passed to cmd.exe safely.");
            return new StepResult(1, "The Windows Update caches could not be reset: the Windows folder name is unusual.");
        }

        var failure = (StepResult?)null;

        async Task RunAsync(string fileName, string arguments, bool mustSucceed = true)
        {
            var result = await system.RunCommandAsync(fileName, arguments, ct);
            if (mustSucceed && result.ExitCode != 0 && failure is null)
            {
                failure = new StepResult(result.ExitCode, $"{fileName} {arguments} failed: {result.Output.Trim()}");
            }
        }

        // A service that is already stopped exits non-zero; that is fine.
        foreach (var service in StopOrder)
        {
            await RunAsync("net", $"stop {service}", mustSucceed: false);
        }

        await RunAsync("cmd.exe", $@"/c del /f /q ""{BitsQueueFiles}""", mustSucceed: false);

        foreach (var (parent, name) in Caches)
        {
            var folder = CommandArgs.QuoteCmd($@"{system.WindowsDirectory}\{parent}\{name}");
            var backup = CommandArgs.QuoteCmd($@"{system.WindowsDirectory}\{parent}\{name}.bak");
            await RunAsync("cmd.exe", $"/c if exist {backup} rd /s /q {backup}");
            await RunAsync("cmd.exe", $"/c if exist {folder} ren {folder} {name}.bak");
        }

        foreach (var (service, startType) in Defaults)
        {
            await RunAsync("sc", $"config {service} start= {startType}");
        }

        // Start again even after a failure, so Windows Update is never left stopped.
        foreach (var service in StartOrder)
        {
            await RunAsync("net", $"start {service}");
        }

        return failure ?? new StepResult(0, "Windows Update components were reset.");
    }
}
