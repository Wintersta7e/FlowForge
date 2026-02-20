using System.Text.Json;
using FluentAssertions;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Transforms;
using FlowForge.Core.Nodes.Base;
using FlowForge.Tests.Helpers;

namespace FlowForge.Tests.Nodes;

public class SortNodeTests
{
    private static Dictionary<string, JsonElement> MakeConfig(object config)
    {
        string json = JsonSerializer.Serialize(config);
        JsonDocument doc = JsonDocument.Parse(json);
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
        var node = new SortNode();
        node.Configure(MakeConfig(new { field = "filename", direction = "asc" }));

        FileJob job = MakeJob(Path.Combine("/tmp", "test.txt"));

        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: false);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FlushAsync_returns_all_buffered_jobs()
    {
        var node = new SortNode();
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
        var node = new SortNode();
        node.Configure(MakeConfig(new { field = "filename", direction = "asc" }));

        FileJob jobB = MakeJob(Path.Combine("/tmp", "b.txt"));
        FileJob jobA = MakeJob(Path.Combine("/tmp", "a.txt"));
        FileJob jobC = MakeJob(Path.Combine("/tmp", "c.txt"));

        await node.TransformAsync(jobB, dryRun: false);
        await node.TransformAsync(jobA, dryRun: false);
        await node.TransformAsync(jobC, dryRun: false);

        List<FileJob> result = (await node.FlushAsync()).ToList();

        result.Should().HaveCount(3);
        result[0].FileName.Should().Be("a.txt");
        result[1].FileName.Should().Be("b.txt");
        result[2].FileName.Should().Be("c.txt");
    }

    [Fact]
    public async Task Sort_by_filename_descending()
    {
        var node = new SortNode();
        node.Configure(MakeConfig(new { field = "filename", direction = "desc" }));

        FileJob jobB = MakeJob(Path.Combine("/tmp", "b.txt"));
        FileJob jobA = MakeJob(Path.Combine("/tmp", "a.txt"));
        FileJob jobC = MakeJob(Path.Combine("/tmp", "c.txt"));

        await node.TransformAsync(jobB, dryRun: false);
        await node.TransformAsync(jobA, dryRun: false);
        await node.TransformAsync(jobC, dryRun: false);

        List<FileJob> result = (await node.FlushAsync()).ToList();

        result.Should().HaveCount(3);
        result[0].FileName.Should().Be("c.txt");
        result[1].FileName.Should().Be("b.txt");
        result[2].FileName.Should().Be("a.txt");
    }

    [Fact]
    public async Task Sort_by_extension_ascending()
    {
        var node = new SortNode();
        node.Configure(MakeConfig(new { field = "extension", direction = "asc" }));

        FileJob jobPng = MakeJob(Path.Combine("/tmp", "file.png"));
        FileJob jobJpg = MakeJob(Path.Combine("/tmp", "file.jpg"));
        FileJob jobTxt = MakeJob(Path.Combine("/tmp", "file.txt"));

        await node.TransformAsync(jobPng, dryRun: false);
        await node.TransformAsync(jobJpg, dryRun: false);
        await node.TransformAsync(jobTxt, dryRun: false);

        List<FileJob> result = (await node.FlushAsync()).ToList();

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

        var node = new SortNode();
        node.Configure(MakeConfig(new { field = "size", direction = "asc" }));

        await node.TransformAsync(MakeJob(largePath), dryRun: false);
        await node.TransformAsync(MakeJob(smallPath), dryRun: false);
        await node.TransformAsync(MakeJob(mediumPath), dryRun: false);

        List<FileJob> result = (await node.FlushAsync()).ToList();

        result.Should().HaveCount(3);
        result[0].FileName.Should().Be("small.txt");
        result[1].FileName.Should().Be("medium.txt");
        result[2].FileName.Should().Be("large.txt");
    }

    [Fact]
    public async Task Second_FlushAsync_returns_empty()
    {
        var node = new SortNode();
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
        var node = new SortNode();
        node.Configure(MakeConfig(new { field = "filename", direction = "asc" }));

        FileJob job = MakeJob(Path.Combine("/tmp", "only.txt"));
        await node.TransformAsync(job, dryRun: false);

        List<FileJob> result = (await node.FlushAsync()).ToList();

        result.Should().HaveCount(1);
        result[0].FileName.Should().Be("only.txt");
    }

    [Fact]
    public async Task FlushAsync_respects_cancellation_token()
    {
        var node = new SortNode();
        node.Configure(MakeConfig(new { field = "filename", direction = "asc" }));

        await node.TransformAsync(MakeJob(Path.Combine("/tmp", "a.txt")), dryRun: false);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => node.FlushAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
