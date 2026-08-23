using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace WindowsCleannerApp;

internal static class AdminHelper
{
    public static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static bool TryRestartElevated(IWin32Window owner, out string? errorMessage)
    {
        errorMessage = null;

        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            errorMessage = "Unable to determine the current executable path.";
            return false;
        }

        var arguments = string.Join(
            " ",
            Environment.GetCommandLineArgs()
                .Skip(1)
                .Select(QuoteArgument));

        var startInfo = new ProcessStartInfo
        {
            FileName = processPath,
            Arguments = arguments,
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = AppContext.BaseDirectory
        };

        try
        {
            Process.Start(startInfo);
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            errorMessage = "The elevation request was canceled.";
            return false;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    private static string QuoteArgument(string argument)
    {
        if (string.IsNullOrEmpty(argument))
        {
            return "\"\"";
        }

        if (!argument.Any(ch => char.IsWhiteSpace(ch) || ch == '"'))
        {
            return argument;
        }

        return "\"" + argument.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
