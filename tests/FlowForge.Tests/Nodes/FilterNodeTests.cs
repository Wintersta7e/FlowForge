using System.Text.Json;
using FluentAssertions;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Transforms;
using FlowForge.Core.Nodes.Base;
using FlowForge.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowForge.Tests.Nodes;

public class FilterNodeTests
{
    private static string FakePath(string filename) => Path.Combine(Path.GetTempPath(), filename);

    private static Dictionary<string, JsonElement> MakeConfig(object conditions)
    {
        string json = JsonSerializer.Serialize(new { conditions });
        var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    [Fact]
    public async Task File_matching_extension_passes()
    {
        var node = new FilterNode(NullLogger<FilterNode>.Instance);
        node.Configure(MakeConfig(new[]
        {
            new { field = "extension", @operator = "equals", value = ".jpg" }
        }));

        var job = new FileJob
        {
            OriginalPath = FakePath("photo.jpg"),
            CurrentPath = FakePath("photo.jpg")
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: true);
        result.Should().HaveCount(1);
        result.First().NodeLog.Should().Contain(l => l.Contains("passed"));
    }

    [Fact]
    public async Task File_not_matching_extension_is_dropped()
    {
        var node = new FilterNode(NullLogger<FilterNode>.Instance);
        node.Configure(MakeConfig(new[]
        {
            new { field = "extension", @operator = "equals", value = ".jpg" }
        }));

        var job = new FileJob
        {
            OriginalPath = FakePath("document.pdf"),
            CurrentPath = FakePath("document.pdf")
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: true);
        result.Should().BeEmpty();
        job.Status.Should().Be(FileJobStatus.Skipped);
    }

    [Fact]
    public async Task Contains_operator_matches_substring()
    {
        var node = new FilterNode(NullLogger<FilterNode>.Instance);
        node.Configure(MakeConfig(new[]
        {
            new { field = "filename", @operator = "contains", value = "photo" }
        }));

        var job = new FileJob
        {
            OriginalPath = FakePath("my_photo_001.jpg"),
            CurrentPath = FakePath("my_photo_001.jpg")
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: true);
        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task Matches_operator_uses_regex()
    {
        var node = new FilterNode(NullLogger<FilterNode>.Instance);
        node.Configure(MakeConfig(new[]
        {
            new { field = "filename", @operator = "matches", value = @"^\d{3}\.jpg$" }
        }));

        var jobMatch = new FileJob
        {
            OriginalPath = FakePath("001.jpg"),
            CurrentPath = FakePath("001.jpg")
        };
        var jobNoMatch = new FileJob
        {
            OriginalPath = FakePath("abc.jpg"),
            CurrentPath = FakePath("abc.jpg")
        };

        IEnumerable<FileJob> resultMatch = await node.TransformAsync(jobMatch, dryRun: true);
        IEnumerable<FileJob> resultNoMatch = await node.TransformAsync(jobNoMatch, dryRun: true);

        resultMatch.Should().HaveCount(1);
        resultNoMatch.Should().BeEmpty();
    }

    [Fact]
    public async Task Size_filter_with_real_file()
    {
        using var dir = new TempDirectory();
        string filePath = Path.Combine(dir.Path, "small.txt");
        File.WriteAllText(filePath, "hello");

        var node = new FilterNode(NullLogger<FilterNode>.Instance);
        node.Configure(MakeConfig(new[]
        {
            new { field = "size", @operator = "lessThan", value = "1000" }
        }));

        var job = new FileJob
        {
            OriginalPath = filePath,
            CurrentPath = filePath
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: false);
        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task Multiple_conditions_all_must_match()
    {
        var node = new FilterNode(NullLogger<FilterNode>.Instance);
        node.Configure(MakeConfig(new[]
        {
            new { field = "extension", @operator = "equals", value = ".jpg" },
            new { field = "filename", @operator = "startsWith", value = "photo" }
        }));

        var jobBoth = new FileJob
        {
            OriginalPath = FakePath("photo_001.jpg"),
            CurrentPath = FakePath("photo_001.jpg")
        };
        var jobExtOnly = new FileJob
        {
            OriginalPath = FakePath("document.jpg"),
            CurrentPath = FakePath("document.jpg")
        };

        IEnumerable<FileJob> resultBoth = await node.TransformAsync(jobBoth, dryRun: true);
        IEnumerable<FileJob> resultExtOnly = await node.TransformAsync(jobExtOnly, dryRun: true);

        resultBoth.Should().HaveCount(1);
        resultExtOnly.Should().BeEmpty();
    }

    [Fact]
    public async Task Regex_timeout_sets_failed_and_returns_empty()
    {
        var node = new FilterNode(NullLogger<FilterNode>.Instance);
        // Catastrophic backtracking pattern: named group still captured with ExplicitCapture
        node.Configure(MakeConfig(new[]
        {
            new { field = "filename", @operator = "matches", value = @"^(?<g>a+)+$" }
        }));

        var job = new FileJob
        {
            OriginalPath = Path.Combine(Path.GetTempPath(), new string('a', 50) + "!.txt"),
            CurrentPath = Path.Combine(Path.GetTempPath(), new string('a', 50) + "!.txt")
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: true);
        result.Should().BeEmpty();
        job.Status.Should().Be(FileJobStatus.Failed);
        job.ErrorMessage.Should().Contain("regex match timed out");
    }

    [Fact]
    public void Missing_conditions_throws()
    {
        var node = new FilterNode(NullLogger<FilterNode>.Instance);
        var config = new Dictionary<string, JsonElement>();

        Action act = () => node.Configure(config);
        act.Should().Throw<NodeConfigurationException>();
    }

    [Fact]
    public async Task CancellationToken_cancelled_throws_OperationCanceledException()
    {
        var node = new FilterNode(NullLogger<FilterNode>.Instance);
        node.Configure(MakeConfig(new[]
        {
            new { field = "extension", @operator = "equals", value = ".jpg" }
        }));

        var job = new FileJob
        {
            OriginalPath = FakePath("test.jpg"),
            CurrentPath = FakePath("test.jpg")
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => node.TransformAsync(job, dryRun: true, ct: cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task NotEquals_operator_drops_matching_file()
    {
        var node = new FilterNode(NullLogger<FilterNode>.Instance);
        node.Configure(MakeConfig(new[]
        {
            new { field = "extension", @operator = "notequals", value = ".tmp" }
        }));

        var jobTmp = new FileJob { OriginalPath = FakePath("a.tmp"), CurrentPath = FakePath("a.tmp") };
        var jobJpg = new FileJob { OriginalPath = FakePath("b.jpg"), CurrentPath = FakePath("b.jpg") };

        IEnumerable<FileJob> resultTmp = await node.TransformAsync(jobTmp, dryRun: true);
        IEnumerable<FileJob> resultJpg = await node.TransformAsync(jobJpg, dryRun: true);

        resultTmp.Should().BeEmpty();
        resultJpg.Should().HaveCount(1);
    }

    [Fact]
    public async Task StartsWith_operator_filters_by_prefix()
    {
        var node = new FilterNode(NullLogger<FilterNode>.Instance);
        node.Configure(MakeConfig(new[]
        {
            new { field = "filename", @operator = "startswith", value = "IMG_" }
        }));

        var jobMatch = new FileJob { OriginalPath = FakePath("IMG_001.jpg"), CurrentPath = FakePath("IMG_001.jpg") };
        var jobMiss = new FileJob { OriginalPath = FakePath("DSC_001.jpg"), CurrentPath = FakePath("DSC_001.jpg") };

        (await node.TransformAsync(jobMatch, dryRun: true)).Should().HaveCount(1);
        (await node.TransformAsync(jobMiss, dryRun: true)).Should().BeEmpty();
    }

    [Fact]
    public async Task EndsWith_operator_filters_by_suffix()
    {
        var node = new FilterNode(NullLogger<FilterNode>.Instance);
        node.Configure(MakeConfig(new[]
        {
            new { field = "filename", @operator = "endswith", value = "_final.jpg" }
        }));

        var jobMatch = new FileJob { OriginalPath = FakePath("photo_final.jpg"), CurrentPath = FakePath("photo_final.jpg") };
        var jobMiss = new FileJob { OriginalPath = FakePath("photo_draft.jpg"), CurrentPath = FakePath("photo_draft.jpg") };

        (await node.TransformAsync(jobMatch, dryRun: true)).Should().HaveCount(1);
        (await node.TransformAsync(jobMiss, dryRun: true)).Should().BeEmpty();
    }

    [Fact]
    public async Task GreaterThan_operator_compares_size()
    {
        using var dir = new TempDirectory();
        string filePath = Path.Combine(dir.Path, "large.txt");
        File.WriteAllText(filePath, new string('x', 500));

        var node = new FilterNode(NullLogger<FilterNode>.Instance);
        node.Configure(MakeConfig(new[]
        {
            new { field = "size", @operator = "greaterthan", value = "100" }
        }));

        var job = new FileJob { OriginalPath = filePath, CurrentPath = filePath };
        (await node.TransformAsync(job, dryRun: false)).Should().HaveCount(1);
    }

    [Fact]
    public void Unknown_operator_throws_InvalidOperationException()
    {
        var node = new FilterNode(NullLogger<FilterNode>.Instance);
        node.Configure(MakeConfig(new[]
        {
            new { field = "extension", @operator = "bogus", value = ".jpg" }
        }));

        var job = new FileJob { OriginalPath = FakePath("a.jpg"), CurrentPath = FakePath("a.jpg") };
        Func<Task> act = () => node.TransformAsync(job, dryRun: true);
        act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Unknown filter operator*");
    }

    [Fact]
    public async Task CreatedAt_field_with_dry_run_returns_min_date()
    {
        var node = new FilterNode(NullLogger<FilterNode>.Instance);
        node.Configure(MakeConfig(new[]
        {
            new { field = "createdAt", @operator = "greaterthan", value = "0001-01-01T00:00:00.0000000" }
        }));

        var job = new FileJob { OriginalPath = FakePath("a.txt"), CurrentPath = FakePath("a.txt") };
        // In dry-run, createdAt returns DateTime.MinValue, so it should NOT be greater than MinValue
        (await node.TransformAsync(job, dryRun: true)).Should().BeEmpty();
    }

    [Fact]
    public async Task ModifiedAt_field_with_real_file_passes()
    {
        using var dir = new TempDirectory();
        string filePath = Path.Combine(dir.Path, "recent.txt");
        File.WriteAllText(filePath, "data");

        var node = new FilterNode(NullLogger<FilterNode>.Instance);
        node.Configure(MakeConfig(new[]
        {
            new { field = "modifiedAt", @operator = "greaterthan", value = "2000-01-01T00:00:00.0000000" }
        }));

        var job = new FileJob { OriginalPath = filePath, CurrentPath = filePath };
        (await node.TransformAsync(job, dryRun: false)).Should().HaveCount(1);
    }

    [Fact]
    public void Conditions_wrong_type_throws()
    {
        var node = new FilterNode(NullLogger<FilterNode>.Instance);
        string json = JsonSerializer.Serialize(new { conditions = "not an array" });
        var doc = JsonDocument.Parse(json);
        var config = doc.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone());

        Action act = () => node.Configure(config);
        act.Should().Throw<NodeConfigurationException>();
    }

    [Fact]
    public void Invalid_regex_at_configure_throws()
    {
        var node = new FilterNode(NullLogger<FilterNode>.Instance);
        Action act = () => node.Configure(MakeConfig(new[]
        {
            new { field = "filename", @operator = "matches", value = "[invalid" }
        }));

        act.Should().Throw<NodeConfigurationException>().WithMessage("*Invalid regex*");
    }
}
