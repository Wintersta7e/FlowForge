using System.Globalization;
using System.IO.Enumeration;
using System.Security.Cryptography;
using FluentAssertions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Helpers;

/// <summary>
/// Records every file, directory and link under a root, the root included: kind, last-write
/// time, link target, and for files the length and SHA-256. Links are recorded by their own
/// target and never followed or hashed. An inaccessible entry fails the snapshot.
/// Never take one while a test traps content opens: hashing opens every file.
/// A directory's last-write time changes when a child is created, so a caller that creates an
/// excluded root under the snapshot root creates it before <see cref="Take"/>.
/// </summary>
internal sealed class TreeSnapshot
{
    private const string RootKey = ".";

    private readonly SortedDictionary<string, Entry> _entries;

    private TreeSnapshot(SortedDictionary<string, Entry> entries)
    {
        _entries = entries;
        EntryCount = entries.Count - 1;
    }

    /// <summary>Entries under the root, the root itself not counted.</summary>
    public int EntryCount { get; }

    public static TreeSnapshot Take(string root, params string[] excludedRelativeRoots)
    {
        string fullRoot = Path.GetFullPath(root);
        bool IsExcluded(string relative) => excludedRelativeRoots.Any(excluded =>
            string.Equals(relative, excluded, StringComparison.Ordinal)
            || relative.StartsWith(excluded + Path.DirectorySeparatorChar, StringComparison.Ordinal));

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.None,
            IgnoreInaccessible = false,
            ReturnSpecialDirectories = false,
        };
        var infos = new FileSystemEnumerable<FileSystemInfo>(
            fullRoot,
            static (ref FileSystemEntry e) => e.ToFileSystemInfo(),
            options)
        {
            ShouldIncludePredicate = (ref FileSystemEntry e) =>
                !IsExcluded(Path.GetRelativePath(fullRoot, e.ToFullPath())),
            ShouldRecursePredicate = (ref FileSystemEntry e) =>
                e.ToFileSystemInfo().LinkTarget == null && !IsExcluded(Path.GetRelativePath(fullRoot, e.ToFullPath())),
        };

        var entries = new SortedDictionary<string, Entry>(StringComparer.Ordinal)
        {
            [RootKey] = Record(new DirectoryInfo(fullRoot)),
        };
        foreach (FileSystemInfo info in infos)
        {
            entries[Path.GetRelativePath(fullRoot, info.FullName)] = Record(info);
        }

        return new TreeSnapshot(entries);
    }

    public static void AssertUnchanged(TreeSnapshot before, TreeSnapshot after, ITestOutputHelper output, string label)
    {
        IReadOnlyList<string> diffs = before.DiffAgainst(after);
        string header = $"[tree] {label} entries={before.EntryCount} diffs={diffs.Count}";
        output.WriteLine(header);
        foreach (string diff in diffs)
        {
            output.WriteLine(diff);
        }

        before.EntryCount.Should().BeGreaterThan(0, "{0} must cover at least one entry", header);
        diffs.Should().BeEmpty("{0}: {1}", header, string.Join(" | ", diffs));
    }

    public IReadOnlyList<string> DiffAgainst(TreeSnapshot after)
    {
        var diffs = new List<string>();
        foreach (string key in _entries.Keys.Union(after._entries.Keys).Order(StringComparer.Ordinal))
        {
            bool inBefore = _entries.TryGetValue(key, out Entry? old);
            bool inAfter = after._entries.TryGetValue(key, out Entry? now);
            if (!inAfter)
            {
                diffs.Add($"removed {key}");
            }
            else if (!inBefore)
            {
                diffs.Add($"added {key}");
            }
            else
            {
                List<string> fields = old!.ChangedFields(now!);
                if (fields.Count > 0)
                {
                    diffs.Add($"changed {key}: {string.Join(", ", fields)}");
                }
            }
        }

        return diffs;
    }

    private static Entry Record(FileSystemInfo info)
    {
        if (info.LinkTarget is string target)
        {
            return new Entry("link", null, target, null, null);
        }

        if (info is FileInfo file)
        {
            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            string sha256 = Convert.ToHexString(SHA256.HashData(stream));
            return new Entry("file", file.LastWriteTimeUtc, null, file.Length, sha256);
        }

        return new Entry("directory", info.LastWriteTimeUtc, null, null, null);
    }

    private sealed record Entry(string Kind, DateTime? LastWriteTimeUtc, string? LinkTarget, long? Length, string? Sha256)
    {
        public List<string> ChangedFields(Entry now)
        {
            var fields = new List<string>();
            Compare(fields, "kind", Kind, now.Kind);
            Compare(fields, "lastWriteTimeUtc", Format(LastWriteTimeUtc), Format(now.LastWriteTimeUtc));
            Compare(fields, "linkTarget", LinkTarget, now.LinkTarget);
            Compare(fields, "length", Length?.ToString(CultureInfo.InvariantCulture), now.Length?.ToString(CultureInfo.InvariantCulture));
            Compare(fields, "sha256", Sha256, now.Sha256);
            return fields;
        }

        private static string? Format(DateTime? time) => time?.ToString("O", CultureInfo.InvariantCulture);

        private static void Compare(List<string> fields, string name, string? old, string? now)
        {
            if (!string.Equals(old, now, StringComparison.Ordinal))
            {
                fields.Add($"{name} {old ?? "-"} -> {now ?? "-"}");
            }
        }
    }
}
