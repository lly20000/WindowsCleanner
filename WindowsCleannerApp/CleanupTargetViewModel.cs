namespace WindowsCleannerApp;

internal sealed class CleanupTargetViewModel
{
    public required CleanupTarget Target { get; init; }

    public bool Selected { get; set; }

    public string Name => Target.Name;

    public string Category => Target.Category;

    public string Location => Target.Path;

    public bool Exists { get; set; }

    public int FileCount { get; set; }

    public long EstimatedBytes { get; set; }

    public string SizeText => SizeFormatter.FormatBytes(EstimatedBytes);

    public string Status { get; set; } = "Waiting";

    public string WarningText { get; set; } = string.Empty;
}
