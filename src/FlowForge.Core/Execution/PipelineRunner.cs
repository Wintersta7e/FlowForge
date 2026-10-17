using System.Diagnostics;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Base;
using FlowForge.Core.Pipeline;
using Microsoft.Extensions.Logging;

namespace FlowForge.Core.Execution;

public class PipelineRunner
{
    /// <summary>
    /// The user's own temp folder on Windows. Elsewhere the temp folder is shared and nothing can tell who owns
    /// a folder in it, so staged copies go to the user's data folder; null when the user has none.
    /// </summary>
    internal static readonly string? DefaultStagingRoot = OperatingSystem.IsWindows()
        ? Path.Combine(Path.GetTempPath(), "FlowForge", "runs")
        : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) is { Length: > 0 } dataFolder
            ? Path.Combine(dataFolder, "FlowForge", "runs")
            : null;

    // A transform step reports its progress at its start, after every this many jobs and at its end.
    private const int StageReportInterval = 25;

    private readonly NodeRegistry _registry;
    private readonly ILogger<PipelineRunner> _logger;
    private readonly int _maxConcurrency;

    /// <summary>Where runs keep staged copies of the files a transform rewrote; null when there is no safe place.</summary>
    private readonly string? _stagingRoot;

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
            GraphValidationResult validation = PipelineGraphValidator.Validate(graph, _registry);
            if (!validation.IsValid)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, validation.Refusals.Select(i => i.Message)));
            }

            (List<(Guid Id, ISourceNode Node)> sources, List<(Guid Id, ITransformNode Node)> transforms, Dictionary<Guid, IOutputNode> outputs) =
                CreateNodes(graph, validation.Order);

            if (!dryRun && _stagingRoot is not null)
            {
                RunContext.SweepStaleRuns(_stagingRoot, _logger);
            }

            IReadOnlyList<Connection> connections = validation.Connections;
            var lineages = new LineageCounter();
            var router = new JobRouter(connections, lineages, context);

            // Reported only once the pipeline is accepted, so a refused one never shows a scan.
            progress?.Report(new PhaseChanged(ExecutionPhase.Enumerating));
            result.TotalFiles = await DiscoverJobsAsync(sources, router, lineages, context, progress, ct).ConfigureAwait(false);

            progress?.Report(new PhaseChanged(ExecutionPhase.Processing));

            int stage = 0;
            foreach ((Guid id, ITransformNode transform) in transforms)
            {
                stage++;
                await ApplyTransformAsync(id, transform, stage, transforms.Count, router, context, result, lineages, progress, ct).ConfigureAwait(false);
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
    /// connections in graph order and each one's jobs in emission order. Every destination is claimed,
    /// one job at a time in that order, before the first delivery starts, so a moved original is checked
    /// against every claim of the run.
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
        var deliveries = new List<Delivery>();
        foreach (Connection connection in connections.Where(c => outputs.ContainsKey(c.ToNode)))
        {
            IOutputNode output = outputs[connection.ToNode];
            foreach (FileJob job in router.CarriedOn(connection))
            {
                ct.ThrowIfCancellationRequested();
                if (TryClaimDestination(job, output, context, result, lineages, progress, out DestinationClaim? claim))
                {
                    deliveries.Add(new Delivery(job, output, claim));
                }
            }
        }

        deliveries = await CopyAsideReplacedInputsAsync(deliveries, context, result, lineages, progress, ct).ConfigureAwait(false);

        var semaphore = new SemaphoreSlim(_maxConcurrency);
        var outputTasks = new List<Task>();

        try
        {
            foreach (Delivery delivery in deliveries)
            {
                await semaphore.WaitAsync(ct).ConfigureAwait(false);
                outputTasks.Add(ConsumeJobAsync(delivery.Job, delivery.Output, delivery.Claim, context, result, lineages, progress, semaphore, ct));
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

    /// <summary>
    /// Before the first delivery, copies every input that a claim of another job overwrites (its destination
    /// or backup path) into the run's staging folder and points the input's readers at the copy, so every
    /// delivery reads the bytes its input had when the run started. A dry run makes the same decision and only
    /// logs it. When the copy fails, the job whose claim would overwrite the input fails instead, and the input
    /// stays untouched. Returns the deliveries still to make.
    /// </summary>
    private async Task<List<Delivery>> CopyAsideReplacedInputsAsync(
        List<Delivery> deliveries,
        RunContext context,
        ExecutionResult result,
        LineageCounter lineages,
        IProgress<PipelineProgressEvent>? progress,
        CancellationToken ct)
    {
        if (!deliveries.Exists(d => d.Claim?.Kind is ClaimKind.Replace or ClaimKind.ReplaceWithBackup))
        {
            return deliveries;
        }

        (Dictionary<string, List<FileJob>> readers, List<FileJob> unresolved) = IndexReaders(deliveries);
        var handled = new HashSet<FileJob>();
        var refused = new HashSet<FileJob>();
        foreach (Delivery delivery in deliveries)
        {
            if (delivery.Claim is not { Kind: ClaimKind.Replace or ClaimKind.ReplaceWithBackup } claim)
            {
                continue;
            }

            bool Eligible(FileJob reader) => reader != delivery.Job && !handled.Contains(reader) && !refused.Contains(reader);
            string[] targets = claim.BackupPath is null ? [claim.Path] : [claim.Path, claim.BackupPath];
            foreach (string target in targets)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var affected = ReadersOf(target, readers, unresolved, Eligible).ToList();
                    await CopyAsideAsync(affected, handled, context, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    FailOutput(delivery.Job, delivery.Output, new IOException(
                        $"'{target}' is an input this run still reads, and it could not be copied aside before '{delivery.Job.OriginalPath}' overwrites it: {ex.Message}",
                        ex));
                    RecordTerminal(delivery.Job, context, lineages, result, progress);
                    refused.Add(delivery.Job);
                    break;
                }
            }
        }

        return deliveries.FindAll(d => !refused.Contains(d.Job));
    }

    /// <summary>
    /// Every delivery's content, resolved through links once and keyed ignoring case. A path that cannot be
    /// resolved may be any file, so its reader counts as reading every file a claim overwrites.
    /// </summary>
    private static (Dictionary<string, List<FileJob>> Readers, List<FileJob> Unresolved) IndexReaders(List<Delivery> deliveries)
    {
        var readers = new Dictionary<string, List<FileJob>>(StringComparer.OrdinalIgnoreCase);
        var unresolved = new List<FileJob>();
        foreach (Delivery delivery in deliveries)
        {
            string resolved;
            try
            {
                resolved = PathGuard.ResolvePath(delivery.Job.ContentPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                unresolved.Add(delivery.Job);
                continue;
            }

            if (!readers.TryGetValue(resolved, out List<FileJob>? list))
            {
                list = [];
                readers[resolved] = list;
            }

            list.Add(delivery.Job);
        }

        return (readers, unresolved);
    }

    /// <summary>The eligible jobs that may read <paramref name="target"/>: one lookup, then a same-file check only on a hit.</summary>
    private static IEnumerable<FileJob> ReadersOf(
        string target,
        Dictionary<string, List<FileJob>> readers,
        List<FileJob> unresolved,
        Func<FileJob, bool> eligible)
    {
        IEnumerable<FileJob> named = readers.TryGetValue(PathGuard.ResolvePath(target), out List<FileJob>? list)
            ? list.Where(reader => eligible(reader) && PathGuard.MayBeSameFile(reader.ContentPath, target))
            : [];
        return named.Concat(unresolved.Where(eligible));
    }

    /// <summary>Points each reader at a staged copy of the file it reads, one copy per file; a dry run only logs it.</summary>
    private static async Task CopyAsideAsync(List<FileJob> affected, HashSet<FileJob> handled, RunContext context, CancellationToken ct)
    {
        foreach (IGrouping<string, FileJob> file in affected.GroupBy(reader => reader.ContentPath, StringComparer.Ordinal))
        {
            if (context.DryRun)
            {
                foreach (FileJob reader in file)
                {
                    reader.NodeLog.Add($"Would copy '{file.Key}' aside first: another delivery of this run overwrites it [dry-run]");
                }
            }
            else
            {
                string copy = await CopyToStagingAsync(file.Key, context, ct).ConfigureAwait(false);
                foreach (FileJob reader in file)
                {
                    context.SetContent(reader, copy);
                }
            }

            handled.UnionWith(file);
        }
    }

    /// <summary>Copies a file into the run's staging folder, keeping its creation and last-write times.</summary>
    private static async Task<string> CopyToStagingAsync(string path, RunContext context, CancellationToken ct)
    {
        string staged = context.NewStagingPath(Path.GetExtension(path));
        FileStream source = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        await using (source.ConfigureAwait(false))
        {
            FileStream copy = new(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await using (copy.ConfigureAwait(false))
            {
                await source.CopyToAsync(copy, ct).ConfigureAwait(false);
            }
        }

        var info = new FileInfo(path);
        File.SetCreationTimeUtc(staged, info.CreationTimeUtc);
        File.SetLastWriteTimeUtc(staged, info.LastWriteTimeUtc);
        return staged;
    }

    private async Task ApplyTransformAsync(
        Guid nodeId,
        ITransformNode transform,
        int stage,
        int stageCount,
        JobRouter router,
        RunContext context,
        ExecutionResult result,
        LineageCounter lineages,
        IProgress<PipelineProgressEvent>? progress,
        CancellationToken ct)
    {
        var given = new HashSet<Guid>();
        IReadOnlyList<FileJob> inputs = router.InputOf(nodeId);
        int done = 0;
        void ReportStage() => progress?.Report(new StageProgress(transform.TypeKey, stage, stageCount, done, inputs.Count));

        foreach (FileJob job in inputs)
        {
            ct.ThrowIfCancellationRequested();
            if (done % StageReportInterval == 0)
            {
                ReportStage();
            }

            done++;
            try
            {
                job.Status = FileJobStatus.Processing;
                IEnumerable<FileJob> transformed = await transform.TransformAsync(job, context, ct).ConfigureAwait(false);
                var transformedList = transformed.ToList();

                if (transformedList.Count == 0)
                {
                    // Buffered nodes intentionally return empty from TransformAsync;
                    // their jobs are routed after FlushAsync.
                    if (transform is IBufferedTransformNode)
                    {
                        given.Add(job.Id);
                    }
                    else
                    {
                        HandleEmptyTransformResult(transform, job, context, result, lineages, progress);
                    }
                }
                else
                {
                    lineages.Join(job, transformedList);
                    foreach (FileJob extra in transformedList.Skip(1))
                    {
                        context.RetainBranch(extra);
                    }

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
            var flushed = (await buffered.FlushAsync(context, ct).ConfigureAwait(false)).ToList();
            EnsureFlushReturnsWhatItWasGiven(transform.TypeKey, nodeId, given, flushed);
            RouteEmitted(transform.TypeKey, nodeId, flushed, router, context, result, lineages, progress);
        }

        ReportStage();
    }

    /// <summary>
    /// The lineage count and each original's branch count know only the jobs a buffered node was given, so its
    /// flush must return exactly those, each once. Anything else stops the run before any of them is delivered.
    /// </summary>
    private static void EnsureFlushReturnsWhatItWasGiven(string typeKey, Guid nodeId, IReadOnlySet<Guid> given, IReadOnlyList<FileJob> flushed)
    {
        var returned = new HashSet<Guid>();
        int foreign = 0;
        foreach (FileJob job in flushed)
        {
            if (!given.Contains(job.Id) || !returned.Add(job.Id))
            {
                foreign++;
            }
        }

        int missing = given.Count - returned.Count;
        if (foreign > 0 || missing > 0)
        {
            throw new InvalidOperationException(
                $"Transform '{typeKey}' ({nodeId}) must return exactly the files it was given when it flushes; it left out {missing} and returned {foreign} it was not given.");
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
            if (context.DryRun && claim is not null)
            {
                context.Destinations.MarkDryRunClaim(job, claim);
            }

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

    /// <summary>One claimed job waiting for its output to deliver it.</summary>
    private sealed record Delivery(FileJob Job, IOutputNode Output, DestinationClaim? Claim);
}
