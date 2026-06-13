namespace WindowsCleannerApp;

internal static class CleanupTargets
{
    public static IReadOnlyList<CleanupTarget> GetDefaultTargets()
    {
        var targets = new List<CleanupTarget>();

        AddIfPresent(targets, "User Temp Files", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp");
        AddIfPresent(targets, "System Temp Files", Environment.GetEnvironmentVariable("WINDIR"), "Temp");
        AddIfPresent(targets, "Windows Update Cache", Environment.GetEnvironmentVariable("WINDIR"), "SoftwareDistribution", "Download");
        AddIfPresent(targets, "Prefetch Cache", Environment.GetEnvironmentVariable("WINDIR"), "Prefetch");
        AddIfPresent(targets, "Crash Dumps", Environment.GetEnvironmentVariable("LOCALAPPDATA"), "CrashDumps");

        return targets;
    }

    private static void AddIfPresent(List<CleanupTarget> targets, string name, params string?[] parts)
    {
        var validParts = parts.Where(part => !string.IsNullOrWhiteSpace(part)).Cast<string>().ToArray();
        if (validParts.Length == 0)
        {
            return;
        }

        var path = System.IO.Path.Combine(validParts);
        targets.Add(new CleanupTarget
        {
            Name = name,
            Path = path,
            Recursive = true
        });
    }
}
