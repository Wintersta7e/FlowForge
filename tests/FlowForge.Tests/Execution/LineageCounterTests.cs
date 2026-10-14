using FlowForge.Core.Execution;
using FlowForge.Core.Models;
using FluentAssertions;

namespace FlowForge.Tests.Execution;

public class LineageCounterTests
{
    [Fact]
    public void Extra_emitted_jobs_join_the_input_lineage()
    {
        var lineages = new LineageCounter();
        var job = new FileJob();
        var extra = new FileJob();

        lineages.AddOriginal(job);
        lineages.Join(job, new[] { job, extra });

        lineages.Complete(job).Should().BeFalse();
        lineages.Complete(extra).Should().BeTrue();
    }

    [Fact]
    public void Branch_keeps_the_lineage_open_until_every_branch_terminates()
    {
        var lineages = new LineageCounter();
        var job = new FileJob();
        var clone = new FileJob();

        lineages.AddOriginal(job);
        lineages.Branch(job, clone);

        lineages.Complete(clone).Should().BeFalse();
        lineages.Complete(job).Should().BeTrue();
    }
}
