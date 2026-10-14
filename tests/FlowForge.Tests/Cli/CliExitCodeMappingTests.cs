using FlowForge.CLI;
using FlowForge.Core.Execution;
using FluentAssertions;

namespace FlowForge.Tests.Cli;

public class CliExitCodeMappingTests
{
    // Columns: totalFiles, succeeded, failed, skipped, expected exit code.
    public static TheoryData<int, int, int, int, int> Cases { get; } = new()
    {
        { 2, 2, 0, 0, 0 },
        { 2, 1, 1, 0, 1 },
        { 2, 0, 2, 0, 2 },
        { 0, 0, 0, 0, 0 },
        { 2, 0, 0, 2, 0 },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Result_maps_to_exit_code(int totalFiles, int succeeded, int failed, int skipped, int expected)
    {
        var result = new ExecutionResult
        {
            TotalFiles = totalFiles,
            Succeeded = succeeded,
            Failed = failed,
            Skipped = skipped,
        };

        CliExitCode.FromResult(result).Should().Be(expected);
    }
}
