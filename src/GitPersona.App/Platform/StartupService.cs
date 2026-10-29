using Microsoft.Win32;

namespace GitPersona.App.Platform;

/// <summary>"Start with Windows" through the per-user Run key, so no elevation is needed.</summary>
public sealed class StartupService
{
    public const string MinimizedArgument = "--minimized";

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    // Task Manager's "Disable" writes here instead of removing the Run value.
    private const string ApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private const string ValueName = "GitPersona";

    public bool IsEnabled
    {
        get
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            if (run?.GetValue(ValueName) is not string)
            {
                return false;
            }

            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath);
            // First byte 0x02 (or no entry) means enabled; odd values mean disabled in Task Manager.
            return approved?.GetValue(ValueName) is not byte[] { Length: > 0 } flags || (flags[0] & 1) == 0;
        }
    }

    public void Enable()
    {
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("The executable path of the current process is unknown.");

        using (var run = Registry.CurrentUser.CreateSubKey(RunKeyPath))
        {
            run.SetValue(ValueName, $"\"{executable}\" {MinimizedArgument}", RegistryValueKind.String);
        }

        // Turning the option on in GitPersona should win over an earlier "Disable" in Task Manager.
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath, writable: true);
        approved?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    public void Disable()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        run?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
