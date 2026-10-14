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

            GraphValidationResult validation = PipelineGraphValidator.Validate(graph, _registry);
            if (!validation.IsValid)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, validation.Refusals.Select(i => i.Message)));
            }

            EnsureSingleChain(graph, validation.Connections);
            (List<NodeDefinition> sourceNodeDefs, List<NodeDefinition> transformNodeDefs, List<NodeDefinition> outputNodeDefs) = ClassifyNodes(graph, validation.Order);

            List<NodeDefinition> orderedTransforms = BuildTransformChain(graph, validation.Order, transformNodeDefs);

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
        IReadOnlyList<Guid> sortedNodeIds)
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
                    // Buffered nodes intentionally return empty from TransformAsync;
                    // their jobs will be counted after FlushAsync.
                    if (transform is not IBufferedTransformNode)
                    {
                        HandleEmptyTransformResult(transform, job, result);
                    }
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
            _logger.LogError("Transform {NodeType} set Failed for {File}: {ErrorMessage}", transform.TypeKey, job.OriginalPath, job.ErrorMessage);
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
                _logger.LogError("Transform {NodeType} flush set Failed for {File}: {ErrorMessage}", typeKey, fj.OriginalPath, fj.ErrorMessage);
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
                    string reason = $"Output '{output.TypeKey}' failed: {ex.Message}";
                    job.NodeLog.Add(reason);
                    job.ErrorMessage ??= reason;
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

    /// <summary>
    /// Refuses any graph that is not one chain from a single source to a single output, naming
    /// every offending node. Repeated connections between the same two nodes count once.
    /// </summary>
    private void EnsureSingleChain(PipelineGraph graph, IReadOnlyList<Connection> connections)
    {
        var incoming = graph.Nodes.ToDictionary(n => n.Id, _ => 0);
        var outgoing = graph.Nodes.ToDictionary(n => n.Id, _ => new List<Guid>());
        foreach ((Guid from, Guid to) in connections.Select(c => (c.FromNode, c.ToNode)).Distinct())
        {
            outgoing[from].Add(to);
            incoming[to]++;
        }

        var reasons = graph.Nodes.ToDictionary(n => n.Id, _ => new List<string>());
        AddDegreeReasons(graph, incoming, outgoing, reasons);
        AddOffPathReasons(graph, incoming, outgoing, reasons);

        string[] offenders = graph.Nodes
            .Where(n => reasons[n.Id].Count > 0)
            .Select(n => $"{n.TypeKey} ({n.Id}): {string.Join("; ", reasons[n.Id])}")
            .ToArray();
        if (offenders.Length > 0)
        {
            throw new InvalidOperationException(
                "Pipeline must be one chain from a single source to a single output. " + string.Join("; ", offenders));
        }
    }

    private void AddDegreeReasons(
        PipelineGraph graph,
        Dictionary<Guid, int> incoming,
        Dictionary<Guid, List<Guid>> outgoing,
        Dictionary<Guid, List<string>> reasons)
    {
        var categories = graph.Nodes.ToDictionary(n => n.Id, n => _registry.GetCategoryForTypeKey(n.TypeKey));
        int sources = categories.Values.Count(c => c == NodeCategory.Source);
        int outputs = categories.Values.Count(c => c == NodeCategory.Output);

        foreach (NodeDefinition node in graph.Nodes)
        {
            NodeCategory category = categories[node.Id];
            int inDegree = incoming[node.Id];
            int outDegree = outgoing[node.Id].Count;
            List<string> nodeReasons = reasons[node.Id];

            AddWhen(nodeReasons, inDegree > 1, "has more than one incoming connection");
            AddWhen(nodeReasons, outDegree > 1, "has more than one outgoing connection");
            AddWhen(nodeReasons, inDegree == 0 && category != NodeCategory.Source, "has no incoming connection but is not a source");
            AddWhen(nodeReasons, inDegree > 0 && category == NodeCategory.Source, "is a source with an incoming connection");
            AddWhen(nodeReasons, sources > 1 && category == NodeCategory.Source, $"is one of {sources} sources");
            AddWhen(nodeReasons, outDegree == 0 && category != NodeCategory.Output, "has no outgoing connection but is not an output");
            AddWhen(nodeReasons, outDegree > 0 && category == NodeCategory.Output, "is an output with an outgoing connection");
            AddWhen(nodeReasons, outputs > 1 && category == NodeCategory.Output, $"is one of {outputs} outputs");
        }
    }

    private static void AddOffPathReasons(
        PipelineGraph graph,
        Dictionary<Guid, int> incoming,
        Dictionary<Guid, List<Guid>> outgoing,
        Dictionary<Guid, List<string>> reasons)
    {
        var starts = graph.Nodes.Where(n => incoming[n.Id] == 0).Select(n => n.Id).ToList();
        if (starts.Count != 1)
        {
            return;
        }

        // Walk the chain from its one start; the graph is acyclic here, so the walk ends.
        var onPath = new HashSet<Guid>();
        for (Guid? current = starts[0]; current is Guid id; current = outgoing[id].Count > 0 ? outgoing[id][0] : null)
        {
            onPath.Add(id);
        }

        foreach (NodeDefinition node in graph.Nodes.Where(n => !onPath.Contains(n.Id)))
        {
            reasons[node.Id].Add("is not on the source-to-output path");
        }
    }

    private static void AddWhen(List<string> reasons, bool condition, string reason)
    {
        if (condition)
        {
            reasons.Add(reason);
        }
    }

    private static List<NodeDefinition> BuildTransformChain(
        PipelineGraph graph,
        IReadOnlyList<Guid> sortedNodeIds,
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
