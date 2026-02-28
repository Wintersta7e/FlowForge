using System.Text.Json;
using FluentAssertions;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Outputs;
using FlowForge.Core.Nodes.Base;
using FlowForge.Tests.Helpers;

namespace FlowForge.Tests.Nodes;

public class FolderOutputNodeTests
{
    private static Dictionary<string, JsonElement> MakeConfig(object config)
    {
        string json = JsonSerializer.Serialize(config);
        JsonDocument doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    [Fact]
    public async Task Copy_mode_preserves_source_file()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("photo.jpg");

        string sourcePath = Path.Combine(dir.Path, "photo.jpg");
        var job = new FileJob { OriginalPath = sourcePath, CurrentPath = sourcePath };

        var node = new FolderOutputNode();
        node.Configure(MakeConfig(new { path = dir.OutputPath, mode = "copy" }));

        await node.ConsumeAsync(job, dryRun: false);

        File.Exists(sourcePath).Should().BeTrue("copy mode should preserve the source file");
        File.Exists(Path.Combine(dir.OutputPath, "photo.jpg")).Should().BeTrue();
    }

    [Fact]
    public async Task Move_mode_removes_source_file()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("photo.jpg");

        string sourcePath = Path.Combine(dir.Path, "photo.jpg");
        var job = new FileJob { OriginalPath = sourcePath, CurrentPath = sourcePath };

        var node = new FolderOutputNode();
        node.Configure(MakeConfig(new { path = dir.OutputPath, mode = "move" }));

        await node.ConsumeAsync(job, dryRun: false);

        File.Exists(sourcePath).Should().BeFalse("move mode should remove the source file");
        File.Exists(Path.Combine(dir.OutputPath, "photo.jpg")).Should().BeTrue();
    }

    [Fact]
    public async Task DryRun_performs_no_file_operations()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("photo.jpg");

        string sourcePath = Path.Combine(dir.Path, "photo.jpg");
        var job = new FileJob { OriginalPath = sourcePath, CurrentPath = sourcePath };

        var node = new FolderOutputNode();
        string destDir = Path.Combine(dir.Path, "nonexistent_output");
        node.Configure(MakeConfig(new { path = destDir, mode = "copy" }));

        await node.ConsumeAsync(job, dryRun: true);

        File.Exists(sourcePath).Should().BeTrue("source should be untouched in dry-run");
        Directory.Exists(destDir).Should().BeFalse("destination directory should not be created in dry-run");
    }

    [Fact]
    public void Missing_path_throws_NodeConfigurationException()
    {
        var node = new FolderOutputNode();
        var config = new Dictionary<string, JsonElement>();

        Action act = () => node.Configure(config);

        act.Should().Throw<NodeConfigurationException>();
    }

    [Fact]
    public void Invalid_mode_throws_NodeConfigurationException()
    {
        var node = new FolderOutputNode();

        Action act = () => node.Configure(MakeConfig(new { path = "/tmp/out", mode = "link" }));

        act.Should().Throw<NodeConfigurationException>()
            .WithMessage("*link*");
    }

    [Fact]
    public async Task Creates_output_directory_if_missing()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("photo.jpg");

        string sourcePath = Path.Combine(dir.Path, "photo.jpg");
        var job = new FileJob { OriginalPath = sourcePath, CurrentPath = sourcePath };

        string newOutputDir = Path.Combine(dir.Path, "brand_new_folder");
        Directory.Exists(newOutputDir).Should().BeFalse("directory should not exist before test");

        var node = new FolderOutputNode();
        node.Configure(MakeConfig(new { path = newOutputDir, mode = "copy" }));

        await node.ConsumeAsync(job, dryRun: false);

        Directory.Exists(newOutputDir).Should().BeTrue();
        File.Exists(Path.Combine(newOutputDir, "photo.jpg")).Should().BeTrue();
    }

    [Fact]
    public async Task Overwrite_true_replaces_existing_file()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("photo.jpg");

        string sourcePath = Path.Combine(dir.Path, "photo.jpg");
        string destPath = Path.Combine(dir.OutputPath, "photo.jpg");
        File.WriteAllText(destPath, "old content");

        var job = new FileJob { OriginalPath = sourcePath, CurrentPath = sourcePath };

        var node = new FolderOutputNode();
        node.Configure(MakeConfig(new { path = dir.OutputPath, mode = "copy", overwrite = true }));

        await node.ConsumeAsync(job, dryRun: false);

        string content = File.ReadAllText(destPath);
        content.Should().Be("test content: photo.jpg");
    }

    [Fact]
    public async Task Overwrite_false_throws_when_destination_exists()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("photo.jpg");

        string sourcePath = Path.Combine(dir.Path, "photo.jpg");
        string destPath = Path.Combine(dir.OutputPath, "photo.jpg");
        File.WriteAllText(destPath, "existing content");

        var job = new FileJob { OriginalPath = sourcePath, CurrentPath = sourcePath };

        var node = new FolderOutputNode();
        node.Configure(MakeConfig(new { path = dir.OutputPath, mode = "copy", overwrite = false }));

        Func<Task> act = async () => await node.ConsumeAsync(job, dryRun: false);

        await act.Should().ThrowAsync<IOException>();
    }

    [Fact]
    public async Task PreserveStructure_places_file_in_matching_subdirectory()
    {
        using var dir = new TempDirectory();
        string sourceBase = Path.Combine(dir.Path, "source");
        string subDir = Path.Combine(sourceBase, "photos", "2024");
        Directory.CreateDirectory(subDir);

        string sourceFile = Path.Combine(subDir, "beach.jpg");
        File.WriteAllText(sourceFile, "image data");

        var job = new FileJob { OriginalPath = sourceFile, CurrentPath = sourceFile };

        var node = new FolderOutputNode();
        node.Configure(MakeConfig(new
        {
            path = dir.OutputPath,
            mode = "copy",
            preserveStructure = true,
            sourceBasePath = sourceBase
        }));

        await node.ConsumeAsync(job, dryRun: false);

        string expectedPath = Path.Combine(dir.OutputPath, "photos", "2024", "beach.jpg");
        File.Exists(expectedPath).Should().BeTrue();
        job.CurrentPath.Should().Be(expectedPath);
    }

    [Fact]
    public async Task CancellationToken_cancelled_throws_OperationCanceledException()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("photo.jpg");

        string sourcePath = Path.Combine(dir.Path, "photo.jpg");
        var job = new FileJob { OriginalPath = sourcePath, CurrentPath = sourcePath };

        var node = new FolderOutputNode();
        node.Configure(MakeConfig(new { path = dir.OutputPath, mode = "copy" }));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => node.ConsumeAsync(job, dryRun: false, ct: cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task NodeLog_updated_after_operation()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("report.txt");

        string sourcePath = Path.Combine(dir.Path, "report.txt");
        var job = new FileJob { OriginalPath = sourcePath, CurrentPath = sourcePath };

        var node = new FolderOutputNode();
        node.Configure(MakeConfig(new { path = dir.OutputPath, mode = "copy" }));

        await node.ConsumeAsync(job, dryRun: false);

        job.NodeLog.Should().ContainSingle()
            .Which.Should().Contain("FolderOutput")
            .And.Contain("copy");
    }

    [Fact]
    public async Task Backup_creates_bak_file_before_overwrite()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("photo.jpg");

        string sourcePath = Path.Combine(dir.Path, "photo.jpg");
        string destPath = Path.Combine(dir.OutputPath, "photo.jpg");
        File.WriteAllText(destPath, "original at destination");

        var job = new FileJob { OriginalPath = sourcePath, CurrentPath = sourcePath };

        var node = new FolderOutputNode();
        node.Configure(MakeConfig(new
        {
            path = dir.OutputPath,
            mode = "copy",
            overwrite = true,
            enableBackup = true,
            backupSuffix = ".bak"
        }));

        await node.ConsumeAsync(job, dryRun: false);

        File.Exists(destPath).Should().BeTrue();
        File.Exists(destPath + ".bak").Should().BeTrue("backup should be created before overwrite");
        File.ReadAllText(destPath + ".bak").Should().Be("original at destination");
    }

    [Fact]
    public async Task Backup_skipped_when_destination_does_not_exist()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("photo.jpg");

        string sourcePath = Path.Combine(dir.Path, "photo.jpg");

        var job = new FileJob { OriginalPath = sourcePath, CurrentPath = sourcePath };

        var node = new FolderOutputNode();
        node.Configure(MakeConfig(new
        {
            path = dir.OutputPath,
            mode = "copy",
            enableBackup = true,
            backupSuffix = ".bak"
        }));

        await node.ConsumeAsync(job, dryRun: false);

        string destPath = Path.Combine(dir.OutputPath, "photo.jpg");
        File.Exists(destPath).Should().BeTrue();
        File.Exists(destPath + ".bak").Should().BeFalse("no backup needed when destination doesn't exist");
    }

    [Fact]
    public async Task Backup_dryrun_logs_but_no_io()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("photo.jpg");

        string sourcePath = Path.Combine(dir.Path, "photo.jpg");
        string destPath = Path.Combine(dir.OutputPath, "photo.jpg");
        File.WriteAllText(destPath, "original");

        var job = new FileJob { OriginalPath = sourcePath, CurrentPath = sourcePath };

        var node = new FolderOutputNode();
        node.Configure(MakeConfig(new
        {
            path = dir.OutputPath,
            mode = "copy",
            overwrite = true,
            enableBackup = true,
            backupSuffix = ".bak"
        }));

        await node.ConsumeAsync(job, dryRun: true);

        File.Exists(destPath + ".bak").Should().BeFalse("dry-run should not create backup");
        job.NodeLog.Should().Contain(s => s.Contains("backup", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Backup_default_suffix_is_bak()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("photo.jpg");

        string sourcePath = Path.Combine(dir.Path, "photo.jpg");
        string destPath = Path.Combine(dir.OutputPath, "photo.jpg");
        File.WriteAllText(destPath, "original");

        var job = new FileJob { OriginalPath = sourcePath, CurrentPath = sourcePath };

        var node = new FolderOutputNode();
        node.Configure(MakeConfig(new
        {
            path = dir.OutputPath,
            mode = "copy",
            overwrite = true,
            enableBackup = true
        }));

        await node.ConsumeAsync(job, dryRun: false);

        File.Exists(destPath + ".bak").Should().BeTrue("default backup suffix should be .bak");
    }
}
