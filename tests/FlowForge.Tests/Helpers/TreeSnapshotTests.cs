using FluentAssertions;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace FlowForge.Tests.Helpers;

public class TreeSnapshotTests
{
    private readonly ITestOutputHelper _output;

    public TreeSnapshotTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Each_change_kind_produces_one_diff()
    {
        (string Expected, Func<string, string, Task> Change)[] kinds =
        [
            ("changed data.bin: sha256 ", (root, _) =>
            {
                string data = Path.Combine(root, "data.bin");
                DateTime written = File.GetLastWriteTimeUtc(data);
                File.WriteAllBytes(data, [9, 8, 7, 6]);
                File.SetLastWriteTimeUtc(data, written);
                return Task.CompletedTask;
            }),
            ("changed data.bin: lastWriteTimeUtc ", (root, _) =>
            {
                string data = Path.Combine(root, "data.bin");
                File.SetLastWriteTimeUtc(data, File.GetLastWriteTimeUtc(data).AddHours(1));
                return Task.CompletedTask;
            }),
            ("added " + Path.Combine("sub", "new"), (root, _) => KeepingParentTimeAsync(
                Path.Combine(root, "sub"),
                () => File.WriteAllBytes(Path.Combine(root, "sub", "new"), []))),
            ("removed .hidden", (root, _) => KeepingParentTimeAsync(
                root,
                () => File.Delete(Path.Combine(root, ".hidden")))),
            ("changed link: linkTarget ", (root, temp) => KeepingParentTimeAsync(root, async () =>
            {
                string link = Path.Combine(root, "link");
                Directory.Delete(link);
                await LinkFactory.CreateDirectoryLinkAsync(link, Path.Combine(temp, "targetB"));
            })),
        ];

        foreach ((string expected, Func<string, string, Task> change) in kinds)
        {
            using var dir = new TempDirectory();
            string root = await BuildFixtureAsync(dir.Path);
            var before = TreeSnapshot.Take(root);
            await change(root, dir.Path);
            IReadOnlyList<string> diffs = before.DiffAgainst(TreeSnapshot.Take(root));

            string listing = $"[{expected}] entries={before.EntryCount} diffs: {string.Join(" | ", diffs)}";
            _output.WriteLine(listing);
            diffs.Should().HaveCount(1, listing);
            diffs[0].Should().StartWith(expected, listing);
        }

        using var emptyDir = new TempDirectory();
        string emptyRoot = Path.Combine(emptyDir.Path, "empty");
        Directory.CreateDirectory(emptyRoot);
        var empty = TreeSnapshot.Take(emptyRoot);

        empty.EntryCount.Should().Be(0);
        Action assertEmpty = () => TreeSnapshot.AssertUnchanged(empty, empty, _output, "empty");
        assertEmpty.Should().Throw<XunitException>().WithMessage("*entries=0*");
    }

    private static async Task<string> BuildFixtureAsync(string temp)
    {
        string root = Path.Combine(temp, "root");
        string targetA = Path.Combine(temp, "targetA");
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        Directory.CreateDirectory(targetA);
        Directory.CreateDirectory(Path.Combine(temp, "targetB"));
        File.WriteAllBytes(Path.Combine(root, "data.bin"), [1, 2, 3, 4]);
        string hidden = Path.Combine(root, ".hidden");
        File.WriteAllText(hidden, "hidden");
        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);
        }

        await LinkFactory.CreateDirectoryLinkAsync(Path.Combine(root, "link"), targetA);
        return root;
    }

    // A directory's last-write time is a recorded field; restoring it keeps each change to one diff.
    private static Task KeepingParentTimeAsync(string parent, Action change) =>
        KeepingParentTimeAsync(parent, () =>
        {
            change();
            return Task.CompletedTask;
        });

    private static async Task KeepingParentTimeAsync(string parent, Func<Task> change)
    {
        DateTime written = Directory.GetLastWriteTimeUtc(parent);
        await change();
        Directory.SetLastWriteTimeUtc(parent, written);
    }
}
