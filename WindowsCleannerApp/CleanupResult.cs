namespace WindowsCleannerApp;

internal sealed class CleanupResult
{
    public required string Name { get; init; }

    public required long DeletedBytes { get; init; }

    public required int DeletedFileCount { get; init; }

    public required IReadOnlyList<string> Errors { get; init; }
}
