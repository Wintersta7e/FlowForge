using FlowForge.Core.Execution;
using FlowForge.Core.Models;
using FlowForge.Core.Pipeline;
using FluentAssertions;

namespace FlowForge.Tests.Execution;

public class JobRouterTests
{
    [Fact]
    public void Fan_out_sends_the_job_first_and_a_joined_clone_to_each_other_connection()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var d = Guid.NewGuid();
        var lineages = new LineageCounter();
        var router = new JobRouter(new[] { Wire(a, b), Wire(a, c), Wire(a, d) }, lineages);
        var job = new FileJob
        {
            OriginalPath = Path.Combine(Path.GetTempPath(), "original.txt"),
            CurrentPath = Path.Combine(Path.GetTempPath(), "renamed.txt"),
            Status = FileJobStatus.Processing,
        };

        lineages.AddOriginal(job);
        router.Emit(job, a);

        router.InputOf(b).Should().ContainSingle().Which.Should().BeSameAs(job);
        FileJob toC = router.InputOf(c).Should().ContainSingle().Subject;
        FileJob toD = router.InputOf(d).Should().ContainSingle().Subject;
        toC.Should().NotBeSameAs(job);
        toD.Should().NotBeSameAs(job);
        new[] { job.Id, toC.Id, toD.Id }.Should().OnlyHaveUniqueItems();
        foreach (FileJob clone in new[] { toC, toD })
        {
            clone.OriginalPath.Should().Be(job.OriginalPath);
            clone.CurrentPath.Should().Be(job.CurrentPath);
            clone.Status.Should().Be(job.Status);
        }

        lineages.Complete(toC).Should().BeFalse();
        lineages.Complete(toD).Should().BeFalse();
        lineages.Complete(job).Should().BeTrue();
    }

    [Fact]
    public void Node_input_follows_connection_order()
    {
        var s1 = Guid.NewGuid();
        var s2 = Guid.NewGuid();
        var t = Guid.NewGuid();
        var lineages = new LineageCounter();
        var router = new JobRouter(new[] { Wire(s2, t), Wire(s1, t) }, lineages);
        var j1 = new FileJob();
        var j2 = new FileJob();
        lineages.AddOriginal(j1);
        lineages.AddOriginal(j2);

        router.Emit(j1, s1);
        router.Emit(j2, s2);

        router.InputOf(t).Should().Equal(j2, j1);
    }

    private static Connection Wire(Guid from, Guid to) => new() { FromNode = from, ToNode = to };
}
