using FlowForge.Core.Pipeline;
using FluentAssertions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Docs;

/// <summary>
/// Shared helpers for the tests that compare a published document with the code that owns each value.
/// Mismatches are written as <c>&lt;repo-relative path&gt;:&lt;line&gt;: &lt;what&gt;</c>.
/// </summary>
internal static class ClaimCheck
{
    /// <summary>
    /// Prints <c>[claims] &lt;slug&gt; scanned=&lt;n&gt; mismatches=&lt;m&gt;</c> and each mismatch, then fails when
    /// nothing was scanned, fewer than <paramref name="floor"/> items were scanned, or any mismatch exists.
    /// </summary>
    internal static void Report(ITestOutputHelper output, string slug, int scanned, int floor, IReadOnlyCollection<string> mismatches)
    {
        output.WriteLine($"[claims] {slug} scanned={scanned} mismatches={mismatches.Count}");
        foreach (string mismatch in mismatches)
        {
            output.WriteLine(mismatch);
        }

        scanned.Should().BeGreaterThan(0, "the {0} check must find something to compare", slug);
        scanned.Should().BeGreaterThanOrEqualTo(floor, "the {0} check scans at least {1} items", slug, floor);
        mismatches.Should().BeEmpty("each document line must match its owner:{0}{1}", Environment.NewLine, string.Join(Environment.NewLine, mismatches));
    }

    /// <summary>
    /// Returns the lines strictly between the first line equal to <paramref name="startMarker"/> and the next line
    /// equal to <paramref name="endMarker"/> (both compared trimmed), with the start marker's 1-based line number,
    /// so body line <c>i</c> is document line <c>StartLine + 1 + i</c>. Returns null when either marker is absent.
    /// </summary>
    internal static (int StartLine, IReadOnlyList<string> Body)? Section(IReadOnlyList<string> lines, string startMarker, string endMarker)
    {
        int start = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            if (string.Equals(lines[i].Trim(), startMarker, StringComparison.Ordinal))
            {
                start = i;
                break;
            }
        }

        if (start < 0)
        {
            return null;
        }

        for (int end = start + 1; end < lines.Count; end++)
        {
            if (string.Equals(lines[end].Trim(), endMarker, StringComparison.Ordinal))
            {
                return (start + 1, lines.Skip(start + 1).Take(end - start - 1).ToList());
            }
        }

        return null;
    }

    /// <summary>
    /// Orders the graph's type keys along its connections (Kahn's algorithm). Ties go to the node listed first in
    /// <see cref="PipelineGraph.Nodes"/>; nodes left on a cycle are appended in <see cref="PipelineGraph.Nodes"/> order.
    /// </summary>
    internal static IReadOnlyList<string> TopologicalTypeKeys(PipelineGraph graph)
    {
        var indexById = new Dictionary<Guid, int>();
        for (int i = 0; i < graph.Nodes.Count; i++)
        {
            indexById.TryAdd(graph.Nodes[i].Id, i);
        }

        List<(int From, int To)> edges = graph.Connections
            .Where(c => indexById.ContainsKey(c.FromNode) && indexById.ContainsKey(c.ToNode))
            .Select(c => (indexById[c.FromNode], indexById[c.ToNode]))
            .ToList();

        int[] inDegree = new int[graph.Nodes.Count];
        foreach ((int _, int to) in edges)
        {
            inDegree[to]++;
        }

        var ready = new SortedSet<int>(Enumerable.Range(0, graph.Nodes.Count).Where(i => inDegree[i] == 0));
        bool[] placed = new bool[graph.Nodes.Count];
        var order = new List<int>(graph.Nodes.Count);
        while (ready.Count > 0)
        {
            int next = ready.Min;
            ready.Remove(next);
            placed[next] = true;
            order.Add(next);
            foreach ((int from, int to) in edges)
            {
                if (from == next && --inDegree[to] == 0)
                {
                    ready.Add(to);
                }
            }
        }

        order.AddRange(Enumerable.Range(0, graph.Nodes.Count).Where(i => !placed[i]));
        return order.Select(i => graph.Nodes[i].TypeKey).ToList();
    }
}
