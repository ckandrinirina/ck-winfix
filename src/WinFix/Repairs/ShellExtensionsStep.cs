using WinFix.System;

namespace WinFix.Repairs;

/// <summary>
/// Disables context-menu handlers whose COM server lives outside the Windows folder by moving them to a
/// backup key. "Non-Microsoft" = InprocServer32 path not under the Windows directory; handlers with no
/// resolvable path are left alone.
/// </summary>
public sealed class ShellExtensionsStep : IRepairStep
{
    public const string BackupRoot = @"HKLM\Software\WinFix\ShellExtensionBackup";

    private const string Classes = @"HKLM\SOFTWARE\Classes";

    private static readonly string[] HandlerKeys =
    [
        $@"{Classes}\*\shellex\ContextMenuHandlers",
        $@"{Classes}\AllFilesystemObjects\shellex\ContextMenuHandlers",
        $@"{Classes}\Directory\shellex\ContextMenuHandlers",
        $@"{Classes}\Directory\Background\shellex\ContextMenuHandlers",
        $@"{Classes}\Folder\shellex\ContextMenuHandlers",
        $@"{Classes}\Drive\shellex\ContextMenuHandlers",
    ];

    public string Name => "Disable add-on shell extensions";

    public Task<StepResult> RunAsync(ISystem system, CancellationToken ct)
    {
        var disabled = 0;
        foreach (var handlersKey in HandlerKeys)
        {
            foreach (var name in system.GetRegistrySubKeyNames(handlersKey))
            {
                ct.ThrowIfCancellationRequested();
                var handlerKey = $@"{handlersKey}\{name}";
                var clsid = system.ReadRegistryValue(handlerKey, string.Empty);
                if (string.IsNullOrWhiteSpace(clsid) || IsMicrosoft(system, clsid))
                {
                    continue;
                }

                var backupKey = $@"{BackupRoot}\{handlerKey.Replace('\\', '|')}";
                system.WriteRegistryValue(backupKey, "Key", handlersKey);
                system.WriteRegistryValue(backupKey, "Name", name);
                system.WriteRegistryValue(backupKey, "Clsid", clsid);
                system.DeleteRegistryKey(handlerKey);
                system.Log($"Disabled shell extension {name} ({clsid}); backup in {backupKey}");
                disabled++;
            }
        }

        return Task.FromResult(new StepResult(0, disabled == 0
            ? "No add-on shell extensions were found."
            : $"{disabled} add-on shell extension(s) were turned off. They can be restored."));
    }

    /// <summary>Puts every backed-up handler back and removes its backup; returns how many were restored.</summary>
    public static int Restore(ISystem system)
    {
        var restored = 0;
        foreach (var entry in system.GetRegistrySubKeyNames(BackupRoot))
        {
            var backupKey = $@"{BackupRoot}\{entry}";
            var key = system.ReadRegistryValue(backupKey, "Key");
            var name = system.ReadRegistryValue(backupKey, "Name");
            var clsid = system.ReadRegistryValue(backupKey, "Clsid");
            if (key is null || name is null || clsid is null)
            {
                continue;
            }

            system.WriteRegistryValue($@"{key}\{name}", string.Empty, clsid);
            system.DeleteRegistryKey(backupKey);
            restored++;
        }

        return restored;
    }

    private static bool IsMicrosoft(ISystem system, string clsid)
    {
        var path = system.ReadRegistryValue($@"{Classes}\CLSID\{clsid}\InprocServer32", string.Empty)?.Trim('"', ' ');
        return string.IsNullOrEmpty(path)
            || path.StartsWith(system.WindowsDirectory.TrimEnd('\\') + @"\", StringComparison.OrdinalIgnoreCase);
    }
}
