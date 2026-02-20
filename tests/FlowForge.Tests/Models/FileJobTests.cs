using FluentAssertions;
using FlowForge.Core.Models;

namespace FlowForge.Tests.Models;

public class FileJobTests
{
    [Fact]
    public void Default_status_is_Pending()
    {
        var job = new FileJob();

        job.Status.Should().Be(FileJobStatus.Pending);
    }

    [Fact]
    public void FileName_computed_from_CurrentPath()
    {
        var job = new FileJob
        {
            CurrentPath = Path.Combine("/tmp", "photo.jpg")
        };

        job.FileName.Should().Be("photo.jpg");
    }

    [Fact]
    public void Extension_always_lowercase()
    {
        var job = new FileJob
        {
            CurrentPath = Path.Combine("/tmp", "photo.JPG")
        };

        job.Extension.Should().Be(".jpg");
    }

    [Fact]
    public void DirectoryName_returns_empty_string_for_empty_path()
    {
        var job = new FileJob
        {
            CurrentPath = string.Empty
        };

        job.DirectoryName.Should().BeEmpty();
    }

    [Fact]
    public void OriginalPath_persists_and_CurrentPath_can_change_independently()
    {
        string original = Path.Combine("/data", "input", "file.txt");
        string updated = Path.Combine("/data", "output", "renamed.txt");

        var job = new FileJob
        {
            OriginalPath = original,
            CurrentPath = original
        };

        job.CurrentPath = updated;

        job.OriginalPath.Should().Be(original);
        job.CurrentPath.Should().Be(updated);
    }

    [Fact]
    public void Metadata_dictionary_accessible()
    {
        var job = new FileJob();

        job.Metadata["camera"] = "Canon EOS R5";
        job.Metadata["iso"] = "400";

        job.Metadata.Should().ContainKey("camera").WhoseValue.Should().Be("Canon EOS R5");
        job.Metadata.Should().ContainKey("iso").WhoseValue.Should().Be("400");
    }

    [Fact]
    public void NodeLog_starts_empty()
    {
        var job = new FileJob();

        job.NodeLog.Should().BeEmpty();
    }

    [Fact]
    public void Unique_Id_per_instance()
    {
        var job1 = new FileJob();
        var job2 = new FileJob();

        job1.Id.Should().NotBe(job2.Id);
    }
}
