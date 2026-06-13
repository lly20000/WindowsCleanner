namespace WindowsCleannerApp;

internal sealed class FileSystemCleaner
{
    public IReadOnlyList<ScanResult> ScanTargets(IReadOnlyList<CleanupTarget> targets)
    {
        var results = new List<ScanResult>(targets.Count);

        foreach (var target in targets)
        {
            results.Add(ScanTarget(target));
        }

        return results;
    }

    public IReadOnlyList<CleanupResult> CleanTargets(IReadOnlyList<ScanResult> scanResults)
    {
        var results = new List<CleanupResult>(scanResults.Count);

        foreach (var scanResult in scanResults)
        {
            results.Add(CleanTarget(scanResult));
        }

        return results;
    }

    private static ScanResult ScanTarget(CleanupTarget target)
    {
        var warnings = new List<string>();

        if (!Directory.Exists(target.Path))
        {
            return new ScanResult
            {
                Name = target.Name,
                Path = target.Path,
                Exists = false,
                EstimatedBytes = 0,
                FileCount = 0,
                Files = Array.Empty<string>(),
                Warnings = warnings,
                Target = target
            };
        }

        var files = new List<string>();
        long totalBytes = 0;

        try
        {
            foreach (var file in EnumerateFilesSafely(target))
            {
                try
                {
                    var info = new FileInfo(file);
                    if (!info.Exists || !target.IsSafeToDelete(file))
                    {
                        continue;
                    }

                    totalBytes += info.Length;
                    files.Add(file);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
                {
                    warnings.Add($"Could not read file info: {file}");
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
        {
            warnings.Add($"Scan failed: {ex.Message}");
        }

        return new ScanResult
        {
            Name = target.Name,
            Path = target.Path,
            Exists = true,
            EstimatedBytes = totalBytes,
            FileCount = files.Count,
            Files = files,
            Warnings = warnings,
            Target = target
        };
    }

    private static CleanupResult CleanTarget(ScanResult scanResult)
    {
        var errors = new List<string>();
        long deletedBytes = 0;
        var deletedCount = 0;

        foreach (var file in scanResult.Files)
        {
            try
            {
                var info = new FileInfo(file);
                var length = info.Exists ? info.Length : 0;

                if (info.Exists)
                {
                    info.IsReadOnly = false;
                    info.Delete();
                    deletedBytes += length;
                    deletedCount++;
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
            {
                errors.Add($"{file} -> {ex.Message}");
            }
        }

        TryDeleteEmptyDirectories(scanResult.Target, errors);

        return new CleanupResult
        {
            Name = scanResult.Name,
            DeletedBytes = deletedBytes,
            DeletedFileCount = deletedCount,
            Errors = errors
        };
    }

    private static IEnumerable<string> EnumerateFilesSafely(CleanupTarget target)
    {
        if (target.DeleteRootFilesOnly || !target.Recursive)
        {
            return Directory.EnumerateFiles(target.Path, "*", SearchOption.TopDirectoryOnly);
        }

        return EnumerateFilesRecursive(target.Path);
    }

    private static IEnumerable<string> EnumerateFilesRecursive(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            string[] files;
            string[] directories;

            try
            {
                files = Directory.GetFiles(current);
            }
            catch
            {
                files = Array.Empty<string>();
            }

            foreach (var file in files)
            {
                yield return file;
            }

            try
            {
                directories = Directory.GetDirectories(current);
            }
            catch
            {
                directories = Array.Empty<string>();
            }

            foreach (var directory in directories)
            {
                pending.Push(directory);
            }
        }
    }

    private static void TryDeleteEmptyDirectories(CleanupTarget target, List<string> errors)
    {
        if (!Directory.Exists(target.Path) || !target.Recursive)
        {
            return;
        }

        var directories = EnumerateDirectoriesBottomUp(target.Path);
        foreach (var directory in directories)
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory, false);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
            {
                errors.Add($"{directory} -> {ex.Message}");
            }
        }
    }

    private static IEnumerable<string> EnumerateDirectoriesBottomUp(string root)
    {
        var allDirectories = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            string[] subdirectories;

            try
            {
                subdirectories = Directory.GetDirectories(current);
            }
            catch
            {
                continue;
            }

            foreach (var subdirectory in subdirectories)
            {
                allDirectories.Add(subdirectory);
                pending.Push(subdirectory);
            }
        }

        allDirectories.Sort((left, right) => right.Length.CompareTo(left.Length));
        return allDirectories;
    }
}
