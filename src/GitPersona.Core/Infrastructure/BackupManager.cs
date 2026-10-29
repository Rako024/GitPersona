using System.Globalization;
using System.Text.RegularExpressions;

namespace GitPersona.Core.Infrastructure;

/// <summary>
/// Creates timestamped copies (<c>config.20261029-153012.bak</c>) next to a file before it is rewritten.
/// Only backups matching that exact naming scheme are ever pruned.
/// </summary>
public sealed class BackupManager
{
    public const int DefaultRetention = 20;

    private const string StampFormat = "yyyyMMdd-HHmmss";

    private readonly TimeProvider _timeProvider;
    private readonly int _retention;

    public BackupManager(TimeProvider? timeProvider = null, int retention = DefaultRetention)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(retention, 1);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _retention = retention;
    }

    /// <returns>The backup path, or <see langword="null"/> when there was no file to back up.</returns>
    public string? Backup(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            return null;
        }

        var stamp = _timeProvider.GetLocalNow().ToString(StampFormat, CultureInfo.InvariantCulture);
        var backupPath = $"{fullPath}.{stamp}.bak";
        for (var counter = 2; File.Exists(backupPath); counter++)
        {
            backupPath = $"{fullPath}.{stamp}-{counter}.bak";
        }

        File.Copy(fullPath, backupPath);
        Prune(fullPath);
        return backupPath;
    }

    /// <summary>Backups of <paramref name="path"/>, newest first.</summary>
    public static IReadOnlyList<string> ListBackups(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var pattern = new Regex(
            $@"^{Regex.Escape(Path.GetFileName(fullPath))}\.(?<stamp>\d{{8}}-\d{{6}})(?:-(?<counter>\d+))?\.bak$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return Directory.EnumerateFiles(directory)
            .Select(file => (file, match: pattern.Match(Path.GetFileName(file))))
            .Where(x => x.match.Success)
            .OrderByDescending(x => x.match.Groups["stamp"].Value, StringComparer.Ordinal)
            .ThenByDescending(x => x.match.Groups["counter"].Success
                ? int.Parse(x.match.Groups["counter"].Value, CultureInfo.InvariantCulture)
                : 1)
            .Select(x => x.file)
            .ToList();
    }

    private void Prune(string fullPath)
    {
        foreach (var stale in ListBackups(fullPath).Skip(_retention))
        {
            File.Delete(stale);
        }
    }
}
