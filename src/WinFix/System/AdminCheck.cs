using System.Security.Principal;

namespace WinFix.System;

/// <summary>Stops the run with exit code 2 and a relaunch hint when not elevated.</summary>
public sealed class AdminCheck(Func<bool> isAdmin)
{
    public const int NotAdminExitCode = 2;

    public static bool IsElevatedOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>True when elevated; otherwise prints how to relaunch and sets <paramref name="exitCode"/>.</summary>
    public bool TryProceed(TextWriter output, out int exitCode)
    {
        if (isAdmin())
        {
            exitCode = 0;
            return true;
        }

        output.WriteLine("WinFix needs administrator rights.");
        output.WriteLine("To relaunch it as administrator:");
        output.WriteLine("1. Press Ctrl+Shift+Esc to open Task Manager.");
        output.WriteLine("2. Click Run new task.");
        output.WriteLine("3. Type the path to WinFix.exe.");
        output.WriteLine("4. Tick \"Create this task with administrative privileges\".");
        output.WriteLine("5. Click OK.");
        exitCode = NotAdminExitCode;
        return false;
    }
}
