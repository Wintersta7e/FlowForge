using System.Diagnostics;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Base;
using FlowForge.Core.Pipeline;
using Microsoft.Extensions.Logging;

namespace FlowForge.Core.Execution;

public class PipelineRunner
{
    private static readonly string DefaultStagingRoot = Path.Combine(Path.GetTempPath(), "FlowForge", "runs");

    private readonly NodeRegistry _registry;
    private readonly ILogger<PipelineRunner> _logger;
    private readonly int _maxConcurrency;

    /// <summary>Where runs keep staged copies of the files a transform rewrote.</summary>
    private readonly string _stagingRoot;

    public PipelineRunner(NodeRegistry registry, ILogger<PipelineRunner> logger, int maxConcurrency = 0, string? stagingRoot = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(logger);
        _registry = registry;
        _logger = logger;
        _maxConcurrency = maxConcurrency > 0 ? maxConcurrency : Environment.ProcessorCount;
        _stagingRoot = stagingRoot ?? DefaultStagingRoot;
    }

    public async Task<ExecutionResult> RunAsync(
        PipelineGraph graph,
        bool dryRun = false,
        IProgress<PipelineProgressEvent>? progress = null,
        CancellationToken ct = default)
    {
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        var stopwatch = Stopwatch.StartNew();
        var result = new ExecutionResult { IsDryRun = dryRun };
        using RunContext context = new(dryRun, _stagingRoot);
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
            (List<(Guid Id, ISourceNode Node)> sources, List<(Guid Id, ITransformNode Node)> transforms, Dictionary<Guid, IOutputNode> outputs) =
                CreateNodes(graph, validation.Order);

            if (!dryRun)
            {
                RunContext.SweepStaleRuns(_stagingRoot, _logger);
            }

            IReadOnlyList<Connection> connections = validation.Connections;
            var lineages = new LineageCounter();
            var router = new JobRouter(connections, lineages);
            result.TotalFiles = await DiscoverJobsAsync(sources, router, lineages, context, progress, ct).ConfigureAwait(false);

            progress?.Report(new PhaseChanged(ExecutionPhase.Processing));

            foreach ((Guid id, ITransformNode transform) in transforms)
            {
                await ApplyTransformAsync(id, transform, router, context, result, lineages, progress, ct).ConfigureAwait(false);
            }

            await DispatchOutputJobsAsync(connections, outputs, router, context, result, lineages, progress, ct).ConfigureAwait(false);

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

    /// <summary>One node per definition, in topological order: every source, then every transform, then every output.</summary>
    private (List<(Guid Id, ISourceNode Node)> Sources, List<(Guid Id, ITransformNode Node)> Transforms, Dictionary<Guid, IOutputNode> Outputs) CreateNodes(
        PipelineGraph graph,
        IReadOnlyList<Guid> order)
    {
        var definitions = graph.Nodes.ToDictionary(n => n.Id);
        var ordered = order.Select(id => definitions[id]).ToList();

        var sources = ordered
            .Where(d => _registry.GetCategoryForTypeKey(d.TypeKey) == NodeCategory.Source)
            .Select(d => (d.Id, Node: _registry.GetSource(d)))
            .ToList();
        var transforms = ordered
            .Where(d => _registry.GetCategoryForTypeKey(d.TypeKey) == NodeCategory.Transform)
            .Select(d => (d.Id, Node: _registry.GetTransform(d)))
            .ToList();
        var outputs = ordered
            .Where(d => _registry.GetCategoryForTypeKey(d.TypeKey) == NodeCategory.Output)
            .ToDictionary(d => d.Id, d => _registry.GetOutput(d));

        return (sources, transforms, outputs);
    }

    private static async Task<int> DiscoverJobsAsync(
        List<(Guid Id, ISourceNode Node)> sources,
        JobRouter router,
        LineageCounter lineages,
        RunContext context,
        IProgress<PipelineProgressEvent>? progress,
        CancellationToken ct)
    {
        int discovered = 0;
        int lastReportedCount = 0;

        foreach ((Guid id, ISourceNode source) in sources)
        {
            await foreach (FileJob job in source.ProduceAsync(ct).ConfigureAwait(false))
            {
                ct.ThrowIfCancellationRequested();
                discovered++;
                job.ContentPath = job.OriginalPath;
                if (!context.DryRun)
                {
                    context.Destinations.RegisterOriginal(job.OriginalPath);
                }

                lineages.AddOriginal(job);
                router.Emit(job, id);

                if (discovered == 1 || discovered - lastReportedCount >= 100)
                {
                    lastReportedCount = discovered;
                    progress?.Report(new FilesDiscovered(discovered));
                }
            }
        }

        if (discovered != lastReportedCount)
        {
            progress?.Report(new FilesDiscovered(discovered));
        }

        return discovered;
    }

    /// <summary>
    /// Hands each job carried on a connection into an output to that output alone, walking those
    /// connections in graph order and each one's jobs in emission order.
    /// </summary>
    private async Task DispatchOutputJobsAsync(
        IReadOnlyList<Connection> connections,
        Dictionary<Guid, IOutputNode> outputs,
        JobRouter router,
        RunContext context,
        ExecutionResult result,
        LineageCounter lineages,
        IProgress<PipelineProgressEvent>? progress,
        CancellationToken ct)
    {
        var semaphore = new SemaphoreSlim(_maxConcurrency);
        var outputTasks = new List<Task>();

        try
        {
            foreach (Connection connection in connections.Where(c => outputs.ContainsKey(c.ToNode)))
            {
                IOutputNode output = outputs[connection.ToNode];
                foreach (FileJob job in router.CarriedOn(connection))
                {
                    ct.ThrowIfCancellationRequested();

                    // Claims run here, one job at a time in input order, before any delivery they decide starts.
                    if (!TryClaimDestination(job, output, context, result, lineages, progress, out DestinationClaim? claim))
                    {
                        continue;
                    }

                    await semaphore.WaitAsync(ct).ConfigureAwait(false);
                    Task task = ConsumeJobAsync(job, output, claim, context, result, lineages, progress, semaphore, ct);
                    outputTasks.Add(task);
                }
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

    private async Task ApplyTransformAsync(
        Guid nodeId,
        ITransformNode transform,
        JobRouter router,
        RunContext context,
        ExecutionResult result,
        LineageCounter lineages,
        IProgress<PipelineProgressEvent>? progress,
        CancellationToken ct)
    {
        foreach (FileJob job in router.InputOf(nodeId))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                job.Status = FileJobStatus.Processing;
                IEnumerable<FileJob> transformed = await transform.TransformAsync(job, context, ct).ConfigureAwait(false);
                var transformedList = transformed.ToList();

                if (transformedList.Count == 0)
                {
                    // Buffered nodes intentionally return empty from TransformAsync;
                    // their jobs are routed after FlushAsync.
                    if (transform is not IBufferedTransformNode)
                    {
                        HandleEmptyTransformResult(transform, job, context, result, lineages, progress);
                    }
                }
                else
                {
                    lineages.Join(job, transformedList);
                    RouteEmitted(transform.TypeKey, nodeId, transformedList, router, context, result, lineages, progress);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                job.Status = FileJobStatus.Failed;
                job.ErrorMessage = ex.Message;
                _logger.LogError(ex, "Transform {NodeType} failed for {File}", transform.TypeKey, job.OriginalPath);
                RecordTerminal(job, context, lineages, result, progress);
            }
        }

        if (transform is IBufferedTransformNode buffered)
        {
            IEnumerable<FileJob> flushed = await buffered.FlushAsync(context, ct).ConfigureAwait(false);
            RouteEmitted(transform.TypeKey, nodeId, flushed, router, context, result, lineages, progress);
        }
    }

    private void HandleEmptyTransformResult(
        ITransformNode transform,
        FileJob job,
        RunContext context,
        ExecutionResult result,
        LineageCounter lineages,
        IProgress<PipelineProgressEvent>? progress)
    {
        if (job.Status == FileJobStatus.Failed)
        {
            _logger.LogError("Transform {NodeType} set Failed for {File}: {ErrorMessage}", transform.TypeKey, job.OriginalPath, job.ErrorMessage);
        }
        else if (job.Status != FileJobStatus.Skipped)
        {
            job.Status = FileJobStatus.Skipped;
            job.NodeLog.Add($"Transform '{transform.TypeKey}' returned empty with no status change — treated as skipped.");
            _logger.LogWarning(
                "Transform {NodeType} returned empty for {File} with status Processing — treating as skipped",
                transform.TypeKey, job.OriginalPath);
        }

        RecordTerminal(job, context, lineages, result, progress);
    }

    /// <summary>A failed or skipped job ends at the node that emitted it; every other job travels on.</summary>
    private void RouteEmitted(
        string typeKey,
        Guid nodeId,
        IEnumerable<FileJob> emitted,
        JobRouter router,
        RunContext context,
        ExecutionResult result,
        LineageCounter lineages,
        IProgress<PipelineProgressEvent>? progress)
    {
        foreach (FileJob job in emitted)
        {
            if (job.Status == FileJobStatus.Failed)
            {
                _logger.LogError("Transform {NodeType} set Failed for {File}: {ErrorMessage}", typeKey, job.OriginalPath, job.ErrorMessage);
                RecordTerminal(job, context, lineages, result, progress);
            }
            else if (job.Status == FileJobStatus.Skipped)
            {
                RecordTerminal(job, context, lineages, result, progress);
            }
            else
            {
                router.Emit(job, nodeId);
            }
        }
    }

    /// <summary>Claims the job's destination; a refusal ends the job as failed and returns false.</summary>
    private bool TryClaimDestination(
        FileJob job,
        IOutputNode output,
        RunContext context,
        ExecutionResult result,
        LineageCounter lineages,
        IProgress<PipelineProgressEvent>? progress,
        out DestinationClaim? claim)
    {
        try
        {
            claim = output.ClaimDestination(job, context);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            claim = null;
            FailOutput(job, output, ex);
            RecordTerminal(job, context, lineages, result, progress);
            return false;
        }
    }

    private async Task ConsumeJobAsync(
        FileJob job,
        IOutputNode output,
        DestinationClaim? claim,
        RunContext context,
        ExecutionResult result,
        LineageCounter lineages,
        IProgress<PipelineProgressEvent>? progress,
        SemaphoreSlim semaphore,
        CancellationToken ct)
    {
        try
        {
            try
            {
                await output.ConsumeAsync(job, claim, context, ct).ConfigureAwait(false);
                job.Status = FileJobStatus.Succeeded;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                FailOutput(job, output, ex);
            }

            RecordTerminal(job, context, lineages, result, progress);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private void FailOutput(FileJob job, IOutputNode output, Exception ex)
    {
        job.Status = FileJobStatus.Failed;
        string reason = $"Output '{output.TypeKey}' failed: {ex.Message}";
        job.NodeLog.Add(reason);
        job.ErrorMessage ??= reason;
        _logger.LogError(ex, "Output node {Node} failed for {File}", output.TypeKey, job.FileName);
    }

    /// <summary>
    /// Records one terminal job: exactly one counter, one <see cref="ExecutionResult.Jobs"/> entry and one
    /// <see cref="FileProcessed"/>, which completes the original when this was its last live branch.
    /// The job's staged content is let go, and a moved original is deleted once every delivery of it is in place.
    /// </summary>
    private void RecordTerminal(
        FileJob job,
        RunContext context,
        LineageCounter lineages,
        ExecutionResult result,
        IProgress<PipelineProgressEvent>? progress)
    {
        context.ReleaseContent(job);
        if (!context.DryRun)
        {
            foreach (string original in context.Destinations.CompleteBranch(job.OriginalPath, job.Status == FileJobStatus.Failed))
            {
                DeleteMovedOriginal(job, original);
            }
        }

        bool completes;
        lock (result)
        {
            switch (job.Status)
            {
                case FileJobStatus.Succeeded:
                    result.Succeeded++;
                    break;
                case FileJobStatus.Failed:
                    result.Failed++;
                    break;
                case FileJobStatus.Skipped:
                    result.Skipped++;
                    break;
                default:
                    throw new UnreachableException($"A terminal job cannot have status {job.Status}.");
            }

            result.Jobs.Add(job);
            completes = lineages.Complete(job);
        }

        progress?.Report(new FileProcessed(job) { CompletesOriginal = completes });
    }

    /// <summary>A failed delete keeps the original; the delivery still counts as succeeded.</summary>
    private void DeleteMovedOriginal(FileJob job, string original)
    {
        try
        {
            File.Delete(original);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Move: could not delete original {Path}", original);
            job.NodeLog.Add($"Move: could not delete original '{Path.GetFileName(original)}': {ex.Message}");
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
}
