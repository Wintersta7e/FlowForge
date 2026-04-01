using System.Diagnostics;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Base;
using FlowForge.Core.Pipeline;
using Microsoft.Extensions.Logging;

namespace FlowForge.Core.Execution;

public class PipelineRunner
{
    private readonly NodeRegistry _registry;
    private readonly ILogger<PipelineRunner> _logger;
    private readonly int _maxConcurrency;

    public PipelineRunner(NodeRegistry registry, ILogger<PipelineRunner> logger, int maxConcurrency = 0)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(logger);
        _registry = registry;
        _logger = logger;
        _maxConcurrency = maxConcurrency > 0 ? maxConcurrency : Environment.ProcessorCount;
    }

    public async Task<ExecutionResult> RunAsync(
        PipelineGraph graph,
        bool dryRun = false,
        IProgress<PipelineProgressEvent>? progress = null,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new ExecutionResult { IsDryRun = dryRun };
        bool succeeded = false;

        try
        {
            progress?.Report(new PhaseChanged(ExecutionPhase.Enumerating));

            ValidateGraph(graph);
            List<Guid> sortedNodeIds = TopologicalSort(graph);
            (List<NodeDefinition> sourceNodeDefs, List<NodeDefinition> transformNodeDefs, List<NodeDefinition> outputNodeDefs) = ClassifyNodes(graph, sortedNodeIds);

            List<NodeDefinition> orderedTransforms = BuildTransformChain(graph, sortedNodeIds, transformNodeDefs);

            var sourceNodes = sourceNodeDefs.Select(d => _registry.GetSource(d)).ToList();
            var transformNodes = orderedTransforms.Select(d => _registry.GetTransform(d)).ToList();
            var outputNodes = outputNodeDefs.Select(d => _registry.GetOutput(d)).ToList();

            List<FileJob> allJobs = await CollectSourceJobsAsync(sourceNodes, progress, ct).ConfigureAwait(false);
            result.TotalFiles = allJobs.Count;

            progress?.Report(new PhaseChanged(ExecutionPhase.Processing));

            IReadOnlyList<FileJob> currentJobs = allJobs;
            foreach (ITransformNode transform in transformNodes)
            {
                currentJobs = await ApplyTransformAsync(transform, currentJobs, dryRun, result, ct).ConfigureAwait(false);
            }

            await DispatchOutputJobsAsync(currentJobs, outputNodes, dryRun, result, progress, ct).ConfigureAwait(false);

            succeeded = true;
        }
        finally
        {
            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;

            _logger.LogInformation(
                "Pipeline {Outcome}: {Total} files, {Succeeded} succeeded, {Failed} failed, {Skipped} skipped ({Duration}ms)",
                succeeded ? "completed" : "aborted",
                result.TotalFiles, result.Succeeded, result.Failed, result.Skipped, result.Duration.TotalMilliseconds);

            if (succeeded)
            {
                progress?.Report(new PhaseChanged(ExecutionPhase.Complete));
            }
        }

        return result;
    }

    private (List<NodeDefinition> Sources, List<NodeDefinition> Transforms, List<NodeDefinition> Outputs) ClassifyNodes(
        PipelineGraph graph,
        List<Guid> sortedNodeIds)
    {
        var sourceNodeDefs = new List<NodeDefinition>();
        var transformNodeDefs = new List<NodeDefinition>();
        var outputNodeDefs = new List<NodeDefinition>();
        var nodeMap = graph.Nodes.ToDictionary(n => n.Id);

        foreach (Guid nodeId in sortedNodeIds)
        {
            NodeDefinition def = nodeMap[nodeId];
            NodeCategory category = _registry.GetCategoryForTypeKey(def.TypeKey);
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

        return (sourceNodeDefs, transformNodeDefs, outputNodeDefs);
    }

    private static async Task<List<FileJob>> CollectSourceJobsAsync(
        List<ISourceNode> sourceNodes,
        IProgress<PipelineProgressEvent>? progress,
        CancellationToken ct)
    {
        var allJobs = new List<FileJob>();
        int lastReportedCount = 0;

        foreach (ISourceNode source in sourceNodes)
        {
            await foreach (FileJob job in source.ProduceAsync(ct).ConfigureAwait(false))
            {
                ct.ThrowIfCancellationRequested();
                allJobs.Add(job);

                if (allJobs.Count == 1 || allJobs.Count - lastReportedCount >= 100)
                {
                    lastReportedCount = allJobs.Count;
                    progress?.Report(new FilesDiscovered(allJobs.Count));
                }
            }
        }

        if (allJobs.Count != lastReportedCount)
        {
            progress?.Report(new FilesDiscovered(allJobs.Count));
        }

        return allJobs;
    }

    private async Task DispatchOutputJobsAsync(
        IReadOnlyList<FileJob> jobs,
        List<IOutputNode> outputNodes,
        bool dryRun,
        ExecutionResult result,
        IProgress<PipelineProgressEvent>? progress,
        CancellationToken ct)
    {
        var semaphore = new SemaphoreSlim(_maxConcurrency);
        var outputTasks = new List<Task>();

        try
        {
            foreach (FileJob job in jobs)
            {
                ct.ThrowIfCancellationRequested();

                if (job.Status == FileJobStatus.Skipped)
                {
                    lock (result)
                    {
                        result.Skipped++;
                        result.Jobs.Add(job);
                    }
                    progress?.Report(new FileProcessed(job));
                    continue;
                }

                await semaphore.WaitAsync(ct).ConfigureAwait(false);
                Task task = ConsumeJobAsync(job, outputNodes, dryRun, result, progress, semaphore, ct);
                outputTasks.Add(task);
            }

            await Task.WhenAll(outputTasks).ConfigureAwait(false);
        }
        catch
        {
            // Drain all in-flight tasks before disposing the semaphore
            // so that Release() calls in ConsumeJobAsync don't hit a disposed object
            if (outputTasks.Count > 0)
            {
                try
                {
                    await Task.WhenAll(outputTasks).ConfigureAwait(false);
                }
                catch
                {
                    // tasks handle their own errors via per-job exception handling
                }
            }

            throw;
        }
        finally
        {
            // Safe to dispose: all tasks are awaited in both normal and error paths above
            semaphore.Dispose();
        }
    }

    private async Task<IReadOnlyList<FileJob>> ApplyTransformAsync(
        ITransformNode transform,
        IReadOnlyList<FileJob> jobs,
        bool dryRun,
        ExecutionResult result,
        CancellationToken ct)
    {
        var nextJobs = new List<FileJob>();

        foreach (FileJob job in jobs)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                job.Status = FileJobStatus.Processing;
                IEnumerable<FileJob> transformed = await transform.TransformAsync(job, dryRun, ct).ConfigureAwait(false);
                var transformedList = transformed.ToList();

                if (transformedList.Count == 0)
                {
                    HandleEmptyTransformResult(transform, job, result);
                }
                else
                {
                    PartitionTransformResults(transform, transformedList, nextJobs, result);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                job.Status = FileJobStatus.Failed;
                job.ErrorMessage = ex.Message;
                _logger.LogError(ex, "Transform {NodeType} failed for {File}", transform.TypeKey, job.OriginalPath);
                lock (result)
                {
                    result.Failed++;
                    result.Jobs.Add(job);
                }
            }
        }

        if (transform is IBufferedTransformNode buffered)
        {
            await FlushBufferedTransformAsync(buffered, transform.TypeKey, nextJobs, result, dryRun, ct).ConfigureAwait(false);
        }

        return nextJobs;
    }

    private void HandleEmptyTransformResult(ITransformNode transform, FileJob job, ExecutionResult result)
    {
        if (job.Status == FileJobStatus.Failed)
        {
            _logger.LogError("Transform {NodeType} set Failed for {File}", transform.TypeKey, job.OriginalPath);
            lock (result)
            {
                result.Failed++;
                result.Jobs.Add(job);
            }
        }
        else if (job.Status == FileJobStatus.Skipped)
        {
            lock (result)
            {
                result.Skipped++;
                result.Jobs.Add(job);
            }
        }
        else
        {
            job.Status = FileJobStatus.Skipped;
            job.NodeLog.Add($"Transform '{transform.TypeKey}' returned empty with no status change — treated as skipped.");
            _logger.LogWarning(
                "Transform {NodeType} returned empty for {File} with status Processing — treating as skipped",
                transform.TypeKey, job.OriginalPath);
            lock (result)
            {
                result.Skipped++;
                result.Jobs.Add(job);
            }
        }
    }

    private void PartitionTransformResults(
        ITransformNode transform,
        List<FileJob> transformedList,
        List<FileJob> nextJobs,
        ExecutionResult result)
    {
        foreach (FileJob tj in transformedList)
        {
            if (tj.Status == FileJobStatus.Failed)
            {
                _logger.LogError("Transform {NodeType} set Failed for {File}", transform.TypeKey, tj.OriginalPath);
                lock (result)
                {
                    result.Failed++;
                    result.Jobs.Add(tj);
                }
            }
            else
            {
                nextJobs.Add(tj);
            }
        }
    }

    private async Task FlushBufferedTransformAsync(
        IBufferedTransformNode buffered,
        string typeKey,
        List<FileJob> nextJobs,
        ExecutionResult result,
        bool dryRun,
        CancellationToken ct)
    {
        IEnumerable<FileJob> flushed = await buffered.FlushAsync(dryRun: dryRun, ct: ct).ConfigureAwait(false);
        foreach (FileJob fj in flushed)
        {
            if (fj.Status == FileJobStatus.Failed)
            {
                _logger.LogError("Transform {NodeType} flush set Failed for {File}", typeKey, fj.OriginalPath);
                lock (result)
                {
                    result.Failed++;
                    result.Jobs.Add(fj);
                }
            }
            else
            {
                nextJobs.Add(fj);
            }
        }
    }

    private async Task ConsumeJobAsync(
        FileJob job,
        List<IOutputNode> outputs,
        bool dryRun,
        ExecutionResult result,
        IProgress<PipelineProgressEvent>? progress,
        SemaphoreSlim semaphore,
        CancellationToken ct)
    {
        try
        {
            bool anyFailed = false;

            foreach (IOutputNode output in outputs)
            {
                try
                {
                    await output.ConsumeAsync(job, dryRun, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    anyFailed = true;
                    job.Status = FileJobStatus.Failed;
                    job.NodeLog.Add($"Output '{output.TypeKey}' failed: {ex.Message}");
                    _logger.LogError(ex, "Output node {Node} failed for {File}", output.TypeKey, job.FileName);
                }
            }

            if (!anyFailed)
            {
                job.Status = FileJobStatus.Succeeded;
            }

            lock (result)
            {
                if (anyFailed)
                {
                    result.Failed++;
                }
                else
                {
                    result.Succeeded++;
                }

                result.Jobs.Add(job);
            }

            progress?.Report(new FileProcessed(job));
        }
        catch (OperationCanceledException)
        {
            throw;
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

        foreach (NodeDefinition node in graph.Nodes)
        {
            if (!_registry.IsRegistered(node.TypeKey))
            {
                throw new InvalidOperationException($"Unknown node type: '{node.TypeKey}'");
            }
        }

        var nodeIds = graph.Nodes.Select(n => n.Id).ToHashSet();
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
        var transformMap = transformNodeDefs.ToDictionary(n => n.Id);
        var ordered = new List<NodeDefinition>();

        foreach (Guid nodeId in sortedNodeIds)
        {
            if (transformMap.TryGetValue(nodeId, out NodeDefinition? def))
            {
                ordered.Add(def);
            }
        }

        return ordered;
    }
}
