namespace WindowsCleannerApp;

internal sealed class ScanResult
{
    public required string Name { get; init; }

    public required string Path { get; init; }

    public required bool Exists { get; init; }

    public required long EstimatedBytes { get; init; }

    public required int FileCount { get; init; }

    public required IReadOnlyList<string> Files { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }

    public required CleanupTarget Target { get; init; }
}
