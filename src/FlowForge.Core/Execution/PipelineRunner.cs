using System.Diagnostics;
using System.Threading.Channels;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Base;
using FlowForge.Core.Pipeline;
using Serilog;

namespace FlowForge.Core.Execution;

public class PipelineRunner
{
    private readonly NodeRegistry _registry;
    private readonly ILogger _logger;
    private readonly int _maxConcurrency;

    public PipelineRunner(NodeRegistry registry, ILogger logger, int maxConcurrency = 0)
    {
        _registry = registry;
        _logger = logger;
        _maxConcurrency = maxConcurrency > 0 ? maxConcurrency : Environment.ProcessorCount;
    }

    public async Task<ExecutionResult> RunAsync(
        PipelineGraph graph,
        bool dryRun = false,
        IProgress<FileJob>? progress = null,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new ExecutionResult { IsDryRun = dryRun };

        // 1. Validate graph
        ValidateGraph(graph);

        // 2. Topological sort
        List<Guid> sortedNodeIds = TopologicalSort(graph);

        // 3. Classify nodes
        var sourceNodeDefs = new List<NodeDefinition>();
        var transformNodeDefs = new List<NodeDefinition>();
        var outputNodeDefs = new List<NodeDefinition>();

        foreach (Guid nodeId in sortedNodeIds)
        {
            NodeDefinition def = graph.Nodes.First(n => n.Id == nodeId);
            NodeCategory category = _registry.GetCategory(def);
            switch (category)
            {
                case NodeCategory.Source:
                    sourceNodeDefs.Add(def);
                    break;
                case NodeCategory.Transform:
                    transformNodeDefs.Add(def);
                    break;
                case NodeCategory.Output:
                    outputNodeDefs.Add(def);
                    break;
            }
        }

        // Build the ordered transform chain based on connections
        List<NodeDefinition> orderedTransforms = BuildTransformChain(graph, sortedNodeIds, transformNodeDefs);

        // 4. Create node instances
        var sourceNodes = sourceNodeDefs.Select(d => _registry.GetSource(d)).ToList();
        var transformNodes = orderedTransforms.Select(d => _registry.GetTransform(d)).ToList();
        var outputNodes = outputNodeDefs.Select(d => _registry.GetOutput(d)).ToList();

        // 5. Execute pipeline
        using var semaphore = new SemaphoreSlim(_maxConcurrency);
        var tasks = new List<Task>();

        foreach (ISourceNode source in sourceNodes)
        {
            await foreach (FileJob job in source.ProduceAsync(ct))
            {
                ct.ThrowIfCancellationRequested();
                result.TotalFiles++;

                await semaphore.WaitAsync(ct);
                Task task = ProcessJobAsync(job, transformNodes, outputNodes, dryRun, result, progress, semaphore, ct);
                tasks.Add(task);
            }
        }

        await Task.WhenAll(tasks);

        stopwatch.Stop();
        result.Duration = stopwatch.Elapsed;

        _logger.Information(
            "Pipeline completed: {Total} files, {Succeeded} succeeded, {Failed} failed, {Skipped} skipped ({Duration}ms)",
            result.TotalFiles, result.Succeeded, result.Failed, result.Skipped, result.Duration.TotalMilliseconds);

        return result;
    }

    private async Task ProcessJobAsync(
        FileJob job,
        List<ITransformNode> transforms,
        List<IOutputNode> outputs,
        bool dryRun,
        ExecutionResult result,
        IProgress<FileJob>? progress,
        SemaphoreSlim semaphore,
        CancellationToken ct)
    {
        try
        {
            job.Status = FileJobStatus.Processing;
            IEnumerable<FileJob> currentJobs = new[] { job };

            // Walk the transform chain
            foreach (ITransformNode transform in transforms)
            {
                var nextJobs = new List<FileJob>();
                foreach (FileJob currentJob in currentJobs)
                {
                    ct.ThrowIfCancellationRequested();
                    IEnumerable<FileJob> transformed = await transform.TransformAsync(currentJob, dryRun, ct);
                    nextJobs.AddRange(transformed);
                }
                currentJobs = nextJobs;
            }

            // Send to output nodes
            foreach (FileJob outputJob in currentJobs)
            {
                foreach (IOutputNode output in outputs)
                {
                    await output.ConsumeAsync(outputJob, dryRun, ct);
                }

                outputJob.Status = FileJobStatus.Succeeded;
                lock (result)
                {
                    result.Succeeded++;
                    result.Jobs.Add(outputJob);
                }
                progress?.Report(outputJob);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            job.Status = FileJobStatus.Failed;
            job.ErrorMessage = ex.Message;
            _logger.Error(ex, "Job failed for {File}", job.OriginalPath);

            lock (result)
            {
                result.Failed++;
                result.Jobs.Add(job);
            }
            progress?.Report(job);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private void ValidateGraph(PipelineGraph graph)
    {
        if (graph.Nodes.Count == 0)
        {
            throw new InvalidOperationException("Pipeline has no nodes.");
        }

        // Check all TypeKeys are registered
        foreach (NodeDefinition node in graph.Nodes)
        {
            if (!_registry.IsRegistered(node.TypeKey))
            {
                throw new InvalidOperationException($"Unknown node type: '{node.TypeKey}'");
            }
        }

        // Check all connections reference valid nodes
        HashSet<Guid> nodeIds = graph.Nodes.Select(n => n.Id).ToHashSet();
        foreach (Connection conn in graph.Connections)
        {
            if (!nodeIds.Contains(conn.FromNode))
            {
                throw new InvalidOperationException($"Connection references unknown source node: {conn.FromNode}");
            }
            if (!nodeIds.Contains(conn.ToNode))
            {
                throw new InvalidOperationException($"Connection references unknown target node: {conn.ToNode}");
            }
        }
    }

    private static List<Guid> TopologicalSort(PipelineGraph graph)
    {
        var inDegree = new Dictionary<Guid, int>();
        var adjacency = new Dictionary<Guid, List<Guid>>();

        foreach (NodeDefinition node in graph.Nodes)
        {
            inDegree[node.Id] = 0;
            adjacency[node.Id] = new List<Guid>();
        }

        foreach (Connection conn in graph.Connections)
        {
            adjacency[conn.FromNode].Add(conn.ToNode);
            inDegree[conn.ToNode]++;
        }

        var queue = new Queue<Guid>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var sorted = new List<Guid>();

        while (queue.Count > 0)
        {
            Guid current = queue.Dequeue();
            sorted.Add(current);

            foreach (Guid neighbor in adjacency[current])
            {
                inDegree[neighbor]--;
                if (inDegree[neighbor] == 0)
                {
                    queue.Enqueue(neighbor);
                }
            }
        }

        if (sorted.Count != graph.Nodes.Count)
        {
            throw new InvalidOperationException("Pipeline graph contains a cycle.");
        }

        return sorted;
    }

    private static List<NodeDefinition> BuildTransformChain(
        PipelineGraph graph,
        List<Guid> sortedNodeIds,
        List<NodeDefinition> transformNodeDefs)
    {
        HashSet<Guid> transformIds = transformNodeDefs.Select(n => n.Id).ToHashSet();
        var ordered = new List<NodeDefinition>();

        foreach (Guid nodeId in sortedNodeIds)
        {
            if (transformIds.Contains(nodeId))
            {
                ordered.Add(transformNodeDefs.First(n => n.Id == nodeId));
            }
        }

        return ordered;
    }
}
