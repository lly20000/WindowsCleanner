namespace WindowsCleannerApp;

internal sealed class CleanupTarget
{
    public required string Name { get; init; }

    public required string Path { get; init; }

    public string Category { get; init; } = "General Cleanup";

    public string Description { get; init; } = string.Empty;

    public bool DefaultSelected { get; init; } = true;

    public bool Recursive { get; init; } = true;

    public bool DeleteRootFilesOnly { get; init; }

    public bool IsSafeToDelete(string filePath)
    {
        var extension = System.IO.Path.GetExtension(filePath);
        return !string.Equals(extension, ".sys", StringComparison.OrdinalIgnoreCase);
    }
}
