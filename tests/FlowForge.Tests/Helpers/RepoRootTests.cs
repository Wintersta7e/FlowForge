using FluentAssertions;

namespace FlowForge.Tests.Helpers;

public class RepoRootTests
{
    [Fact]
    public void Find_throws_naming_the_start_directory_when_no_solution_is_above_it()
    {
        // Precondition: the temp root has no FlowForge.sln above it. If TMPDIR points
        // inside a checkout this test fails, by design.
        using var dir = new TempDirectory();

        Action act = () => RepoRoot.Find(dir.Path);

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{dir.Path}*");
    }
}
