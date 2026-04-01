using System.Text.Json;
using FluentAssertions;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Transforms;
using FlowForge.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowForge.Tests.Nodes;

public class SortNodeTests
{
    private static Dictionary<string, JsonElement> MakeConfig(object config)
    {
        string json = JsonSerializer.Serialize(config);
        var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    private static FileJob MakeJob(string filePath)
    {
        return new FileJob { OriginalPath = filePath, CurrentPath = filePath };
    }

    [Fact]
    public async Task TransformAsync_returns_empty_because_it_buffers()
    {
        var node = new SortNode(NullLogger<SortNode>.Instance);
        node.Configure(MakeConfig(new { field = "filename", direction = "asc" }));

        FileJob job = MakeJob(Path.Combine("/tmp", "test.txt"));

        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: false);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FlushAsync_returns_all_buffered_jobs()
    {
        var node = new SortNode(NullLogger<SortNode>.Instance);
        node.Configure(MakeConfig(new { field = "filename", direction = "asc" }));

        FileJob job1 = MakeJob(Path.Combine("/tmp", "b.txt"));
        FileJob job2 = MakeJob(Path.Combine("/tmp", "a.txt"));
        FileJob job3 = MakeJob(Path.Combine("/tmp", "c.txt"));

        await node.TransformAsync(job1, dryRun: false);
        await node.TransformAsync(job2, dryRun: false);
        await node.TransformAsync(job3, dryRun: false);

        IEnumerable<FileJob> result = await node.FlushAsync();

        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task Sort_by_filename_ascending()
    {
        var node = new SortNode(NullLogger<SortNode>.Instance);
        node.Configure(MakeConfig(new { field = "filename", direction = "asc" }));

        FileJob jobB = MakeJob(Path.Combine("/tmp", "b.txt"));
        FileJob jobA = MakeJob(Path.Combine("/tmp", "a.txt"));
        FileJob jobC = MakeJob(Path.Combine("/tmp", "c.txt"));

        await node.TransformAsync(jobB, dryRun: false);
        await node.TransformAsync(jobA, dryRun: false);
        await node.TransformAsync(jobC, dryRun: false);

        var result = (await node.FlushAsync()).ToList();

        result.Should().HaveCount(3);
        result[0].FileName.Should().Be("a.txt");
        result[1].FileName.Should().Be("b.txt");
        result[2].FileName.Should().Be("c.txt");
    }

    [Fact]
    public async Task Sort_by_filename_descending()
    {
        var node = new SortNode(NullLogger<SortNode>.Instance);
        node.Configure(MakeConfig(new { field = "filename", direction = "desc" }));

        FileJob jobB = MakeJob(Path.Combine("/tmp", "b.txt"));
        FileJob jobA = MakeJob(Path.Combine("/tmp", "a.txt"));
        FileJob jobC = MakeJob(Path.Combine("/tmp", "c.txt"));

        await node.TransformAsync(jobB, dryRun: false);
        await node.TransformAsync(jobA, dryRun: false);
        await node.TransformAsync(jobC, dryRun: false);

        var result = (await node.FlushAsync()).ToList();

        result.Should().HaveCount(3);
        result[0].FileName.Should().Be("c.txt");
        result[1].FileName.Should().Be("b.txt");
        result[2].FileName.Should().Be("a.txt");
    }

    [Fact]
    public async Task Sort_by_extension_ascending()
    {
        var node = new SortNode(NullLogger<SortNode>.Instance);
        node.Configure(MakeConfig(new { field = "extension", direction = "asc" }));

        FileJob jobPng = MakeJob(Path.Combine("/tmp", "file.png"));
        FileJob jobJpg = MakeJob(Path.Combine("/tmp", "file.jpg"));
        FileJob jobTxt = MakeJob(Path.Combine("/tmp", "file.txt"));

        await node.TransformAsync(jobPng, dryRun: false);
        await node.TransformAsync(jobJpg, dryRun: false);
        await node.TransformAsync(jobTxt, dryRun: false);

        var result = (await node.FlushAsync()).ToList();

        result.Should().HaveCount(3);
        result[0].FileName.Should().Be("file.jpg");
        result[1].FileName.Should().Be("file.png");
        result[2].FileName.Should().Be("file.txt");
    }

    [Fact]
    public async Task Sort_by_size_ascending()
    {
        using var dir = new TempDirectory();

        string smallPath = Path.Combine(dir.Path, "small.txt");
        string mediumPath = Path.Combine(dir.Path, "medium.txt");
        string largePath = Path.Combine(dir.Path, "large.txt");

        File.WriteAllText(smallPath, "a");
        File.WriteAllText(mediumPath, new string('b', 500));
        File.WriteAllText(largePath, new string('c', 5000));

        var node = new SortNode(NullLogger<SortNode>.Instance);
        node.Configure(MakeConfig(new { field = "size", direction = "asc" }));

        await node.TransformAsync(MakeJob(largePath), dryRun: false);
        await node.TransformAsync(MakeJob(smallPath), dryRun: false);
        await node.TransformAsync(MakeJob(mediumPath), dryRun: false);

        var result = (await node.FlushAsync()).ToList();

        result.Should().HaveCount(3);
        result[0].FileName.Should().Be("small.txt");
        result[1].FileName.Should().Be("medium.txt");
        result[2].FileName.Should().Be("large.txt");
    }

    [Fact]
    public async Task Second_FlushAsync_returns_empty()
    {
        var node = new SortNode(NullLogger<SortNode>.Instance);
        node.Configure(MakeConfig(new { field = "filename", direction = "asc" }));

        await node.TransformAsync(MakeJob(Path.Combine("/tmp", "a.txt")), dryRun: false);
        await node.TransformAsync(MakeJob(Path.Combine("/tmp", "b.txt")), dryRun: false);

        IEnumerable<FileJob> firstFlush = await node.FlushAsync();
        firstFlush.Should().HaveCount(2);

        IEnumerable<FileJob> secondFlush = await node.FlushAsync();
        secondFlush.Should().BeEmpty();
    }

    [Fact]
    public async Task Single_job_buffer_returns_one_job()
    {
        var node = new SortNode(NullLogger<SortNode>.Instance);
        node.Configure(MakeConfig(new { field = "filename", direction = "asc" }));

        FileJob job = MakeJob(Path.Combine("/tmp", "only.txt"));
        await node.TransformAsync(job, dryRun: false);

        var result = (await node.FlushAsync()).ToList();

        result.Should().HaveCount(1);
        result[0].FileName.Should().Be("only.txt");
    }

    [Fact]
    public async Task FlushAsync_respects_cancellation_token()
    {
        var node = new SortNode(NullLogger<SortNode>.Instance);
        node.Configure(MakeConfig(new { field = "filename", direction = "asc" }));

        await node.TransformAsync(MakeJob(Path.Combine("/tmp", "a.txt")), dryRun: false);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => node.FlushAsync(ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task FlushAsync_exception_marks_all_jobs_failed()
    {
        var node = new SortNode(NullLogger<SortNode>.Instance);
        // "size" field requires files to exist on disk — non-existent paths will cause an exception
        // during OrderBy when it tries to read file sizes
        node.Configure(MakeConfig(new { field = "size", direction = "asc" }));

        // Buffer jobs with non-existent paths — GetFileSize returns 0 for missing files,
        // so we use an invalid sort field instead
        var node2 = new SortNode(NullLogger<SortNode>.Instance);
        node2.Configure(MakeConfig(new { field = "INVALID_FIELD", direction = "asc" }));

        FileJob job1 = MakeJob(Path.Combine("/tmp", "a.txt"));
        FileJob job2 = MakeJob(Path.Combine("/tmp", "b.txt"));

        await node2.TransformAsync(job1, dryRun: false);
        await node2.TransformAsync(job2, dryRun: false);

        IEnumerable<FileJob> result = await node2.FlushAsync();
        var resultList = result.ToList();

        resultList.Should().HaveCount(2);
        resultList.Should().AllSatisfy(j =>
        {
            j.Status.Should().Be(FileJobStatus.Failed);
            j.ErrorMessage.Should().Contain("Sort: failed during flush");
        });
    }

    [Fact]
    public async Task FlushAsync_dryRun_sorts_without_file_IO()
    {
        var node = new SortNode(NullLogger<SortNode>.Instance);
        node.Configure(MakeConfig(new { field = "size", direction = "asc" }));

        // Non-existent files — dry-run should not try to read file sizes from disk
        FileJob job1 = MakeJob(Path.Combine("/nonexistent", "large.txt"));
        FileJob job2 = MakeJob(Path.Combine("/nonexistent", "small.txt"));
        FileJob job3 = MakeJob(Path.Combine("/nonexistent", "medium.txt"));

        await node.TransformAsync(job1, dryRun: true);
        await node.TransformAsync(job2, dryRun: true);
        await node.TransformAsync(job3, dryRun: true);

        // Should not throw even though files don't exist (dry-run returns default size 0)
        IEnumerable<FileJob> result = await node.FlushAsync(dryRun: true);
        var resultList = result.ToList();

        resultList.Should().HaveCount(3);
        resultList.Should().AllSatisfy(j =>
        {
            j.Status.Should().NotBe(FileJobStatus.Failed);
        });
    }

    [Fact]
    public async Task Sort_by_modifiedAt_ascending()
    {
        using var dir = new TempDirectory();
        string fileA = Path.Combine(dir.Path, "old.txt");
        string fileB = Path.Combine(dir.Path, "new.txt");
        File.WriteAllText(fileA, "a");
        File.WriteAllText(fileB, "b");
        File.SetLastWriteTimeUtc(fileA, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(fileB, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var node = new SortNode(NullLogger<SortNode>.Instance);
        node.Configure(MakeConfig(new { field = "modifiedAt", direction = "asc" }));

        // Add in reverse order
        await node.TransformAsync(MakeJob(fileB), dryRun: false);
        await node.TransformAsync(MakeJob(fileA), dryRun: false);

        IEnumerable<FileJob> result = await node.FlushAsync(dryRun: false);
        var resultList = result.ToList();

        resultList.Should().HaveCount(2);
        resultList[0].FileName.Should().Be("old.txt");
        resultList[1].FileName.Should().Be("new.txt");
    }

    [Fact]
    public async Task Sort_by_createdAt_descending()
    {
        using var dir = new TempDirectory();
        string fileA = Path.Combine(dir.Path, "older.txt");
        string fileB = Path.Combine(dir.Path, "newer.txt");
        File.WriteAllText(fileA, "a");
        File.WriteAllText(fileB, "b");
        File.SetCreationTimeUtc(fileA, new DateTime(2020, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetCreationTimeUtc(fileB, new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        var node = new SortNode(NullLogger<SortNode>.Instance);
        node.Configure(MakeConfig(new { field = "createdAt", direction = "desc" }));

        await node.TransformAsync(MakeJob(fileA), dryRun: false);
        await node.TransformAsync(MakeJob(fileB), dryRun: false);

        IEnumerable<FileJob> result = await node.FlushAsync(dryRun: false);
        var resultList = result.ToList();

        resultList.Should().HaveCount(2);
        resultList[0].FileName.Should().Be("newer.txt");
        resultList[1].FileName.Should().Be("older.txt");
    }
}
