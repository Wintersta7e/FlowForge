using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlowForge.Core.DependencyInjection;
using FlowForge.Core.Execution;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Base;
using FlowForge.Core.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;

var rootCommand = new RootCommand("FlowForge — node-based file processing pipeline runner");

var pipelineArgument = new Argument<FileInfo>("pipeline")
{
    Description = "Path to the .ffpipe pipeline file"
};

var inputOption = new Option<DirectoryInfo?>("--input")
{
    Description = "Override the first FolderInput node's source folder"
};

var outputOption = new Option<DirectoryInfo?>("--output")
{
    Description = "Override the first FolderOutput node's destination folder"
};

var dryRunOption = new Option<bool>("--dry-run")
{
    Description = "Simulate the pipeline without writing any files"
};

var verboseOption = new Option<bool>("--verbose")
{
    Description = "Print per-file log entries to stdout"
};

var formatOption = new Option<string>("--format")
{
    Description = "Output format: text (human-readable) or json (machine-readable)",
    DefaultValueFactory = _ => "text",
};
formatOption.AcceptOnlyFromAmong("text", "json");

var runCommand = new Command("run", "Execute a FlowForge pipeline");
runCommand.Arguments.Add(pipelineArgument);
runCommand.Options.Add(inputOption);
runCommand.Options.Add(outputOption);
runCommand.Options.Add(dryRunOption);
runCommand.Options.Add(verboseOption);
runCommand.Options.Add(formatOption);

runCommand.SetAction(async (parseResult, cancellationToken) =>
{
    FileInfo pipelineFile = parseResult.GetValue(pipelineArgument)!;
    DirectoryInfo? inputDir = parseResult.GetValue(inputOption);
    DirectoryInfo? outputDir = parseResult.GetValue(outputOption);
    bool dryRun = parseResult.GetValue(dryRunOption);
    bool verbose = parseResult.GetValue(verboseOption);
    string format = parseResult.GetValue(formatOption)!;

    int exitCode = await RunPipelineAsync(pipelineFile, inputDir, outputDir, dryRun, verbose, format, cancellationToken).ConfigureAwait(false);
    Environment.ExitCode = exitCode;
});

rootCommand.Subcommands.Add(runCommand);

return rootCommand.Parse(args).Invoke();

static async Task<int> RunPipelineAsync(
    FileInfo pipelineFile,
    DirectoryInfo? inputDir,
    DirectoryInfo? outputDir,
    bool dryRun,
    bool verbose,
    string format,
    CancellationToken cancellationToken)
{
    bool jsonMode = format.Equals("json", StringComparison.OrdinalIgnoreCase);
    TextWriter statusWriter = jsonMode ? Console.Error : Console.Out;

    ConfigureLogging(verbose, jsonMode);

    var services = new ServiceCollection();
    services.AddLogging(builder => builder.AddSerilog(dispose: true));
    services.AddFlowForgeCore();
    // dispose: true on AddSerilog ensures Serilog flushes when the ServiceProvider is disposed
    await using ServiceProvider sp = services.BuildServiceProvider();

    // Resolve runner from DI container before the try block so that DI failures
    // are not caught by the InvalidOperationException handler below.
    PipelineRunner runner = sp.GetRequiredService<PipelineRunner>();

    try
    {
        if (!pipelineFile.Exists)
        {
            Console.Error.WriteLine($"Pipeline file not found: '{pipelineFile.FullName}'");
            return 2;
        }

        PipelineGraph? graph = await LoadAndConfigurePipelineAsync(
            pipelineFile, inputDir, outputDir, dryRun, statusWriter, cancellationToken).ConfigureAwait(false);

        if (graph is null)
        {
            return 2;
        }

        IProgress<PipelineProgressEvent> progress = CreateProgressReporter(statusWriter, verbose);

        ExecutionResult result = await runner.RunAsync(graph, dryRun, progress, cancellationToken).ConfigureAwait(false);

        PrintSummary(result, statusWriter, jsonMode);

        return ToExitCode(result);
    }
    catch (OperationCanceledException)
    {
        Console.Error.WriteLine("Pipeline execution was cancelled.");
        return 2;
    }
    catch (PipelineLoadException ex)
    {
        Console.Error.WriteLine($"Failed to load pipeline: {ex.Message}");
        return 2;
    }
    catch (FileNotFoundException ex)
    {
        Console.Error.WriteLine($"File not found: {ex.Message}");
        return 2;
    }
    catch (DirectoryNotFoundException ex)
    {
        Console.Error.WriteLine($"Directory not found: {ex.Message}");
        return 2;
    }
    catch (NodeConfigurationException ex)
    {
        Console.Error.WriteLine($"Configuration error: {ex.Message}");
        return 2;
    }
    catch (InvalidOperationException ex)
    {
        // Pipeline validation errors (e.g. missing connections, invalid config).
        // DI resolution failures won't reach here — GetRequiredService is called above the try block.
        Console.Error.WriteLine($"Pipeline error: {ex.Message}");
        return 2;
    }
}

// Configure Serilog — route to stderr in JSON mode to keep stdout clean for machine parsing.
static void ConfigureLogging(bool verbose, bool jsonMode)
{
    LogEventLevel minimumLevel = verbose ? LogEventLevel.Information : LogEventLevel.Warning;
    LoggerConfiguration logConfig = new LoggerConfiguration()
        .MinimumLevel.Is(minimumLevel);

    if (jsonMode)
    {
        logConfig.WriteTo.Console(
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
            formatProvider: CultureInfo.InvariantCulture,
            standardErrorFromLevel: LogEventLevel.Verbose);
    }
    else
    {
        logConfig.WriteTo.Console(
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
            formatProvider: CultureInfo.InvariantCulture);
    }

    Log.Logger = logConfig.CreateLogger();
}

// Load the pipeline graph and apply input/output/dry-run overrides.
// Returns the configured graph, or null if a node override target was not found.
static async Task<PipelineGraph?> LoadAndConfigurePipelineAsync(
    FileInfo pipelineFile,
    DirectoryInfo? inputDir,
    DirectoryInfo? outputDir,
    bool dryRun,
    TextWriter statusWriter,
    CancellationToken cancellationToken)
{
    string fullPath = pipelineFile.FullName;
    statusWriter.WriteLine($"Loading pipeline: {fullPath}");

    PipelineGraph graph = await PipelineSerializer.LoadAsync(fullPath, cancellationToken).ConfigureAwait(false);
    statusWriter.WriteLine($"Pipeline '{graph.Name}' loaded ({graph.Nodes.Count} nodes, {graph.Connections.Count} connections)");

    if (inputDir is not null && !ApplyNodeOverride(graph, "FolderInput", "path", inputDir.FullName, "Input", statusWriter))
    {
        return null;
    }

    if (outputDir is not null && !ApplyNodeOverride(graph, "FolderOutput", "path", outputDir.FullName, "Output", statusWriter))
    {
        return null;
    }

    if (dryRun)
    {
        statusWriter.WriteLine("Mode: DRY RUN (no files will be written)");
    }

    statusWriter.WriteLine();
    return graph;
}

// Find a node by type key and override a config value. Returns false if the node was not found.
static bool ApplyNodeOverride(
    PipelineGraph graph,
    string typeKey,
    string configKey,
    string value,
    string label,
    TextWriter statusWriter)
{
    NodeDefinition? node = graph.Nodes.FirstOrDefault(n =>
        n.TypeKey.Equals(typeKey, StringComparison.Ordinal));

    if (node is null)
    {
        Console.Error.WriteLine($"No {typeKey} node found in pipeline to override.");
        return false;
    }

    node.Config[configKey] = JsonSerializer.SerializeToElement(value);
    statusWriter.WriteLine($"{label} override: {value}");
    return true;
}

// Create a progress reporter that writes execution status to the given writer.
static IProgress<PipelineProgressEvent> CreateProgressReporter(TextWriter statusWriter, bool verbose)
{
    int discoveredCount = 0;
    object outputLock = new object();

    return new Progress<PipelineProgressEvent>(evt =>
    {
        lock (outputLock)
        {
            switch (evt)
            {
                case PhaseChanged { Phase: ExecutionPhase.Enumerating }:
                    statusWriter.Write("Scanning...");
                    break;

                case FilesDiscovered discovered:
                    discoveredCount = discovered.TotalCount;
                    statusWriter.Write($"\rScanning... {discovered.TotalCount} files found");
                    break;

                case PhaseChanged { Phase: ExecutionPhase.Processing }:
                    statusWriter.WriteLine();
                    statusWriter.WriteLine($"Processing {discoveredCount} files...");
                    statusWriter.WriteLine();
                    break;

                case FileProcessed { Job: var job }:
                    PrintFileResult(statusWriter, job, verbose);
                    break;

                case PhaseChanged { Phase: ExecutionPhase.Complete }:
                    break;
            }
        }
    });
}

// Print the status line (and optional detail) for a single processed file.
static void PrintFileResult(TextWriter statusWriter, FileJob job, bool verbose)
{
    string statusIcon = job.Status switch
    {
        FileJobStatus.Succeeded => "[OK]",
        FileJobStatus.Failed => "[FAIL]",
        FileJobStatus.Skipped => "[SKIP]",
        _ => "[??]"
    };

    statusWriter.WriteLine($"  {statusIcon} {Path.GetFileName(job.OriginalPath)}");

    if (verbose)
    {
        foreach (string logEntry in job.NodeLog)
        {
            statusWriter.WriteLine($"        {logEntry}");
        }
    }

    if (job.Status == FileJobStatus.Failed && job.ErrorMessage is not null)
    {
        Console.Error.WriteLine($"        Error: {job.ErrorMessage}");
    }
}

// Print the pipeline execution summary to the status writer.
static void PrintSummary(ExecutionResult result, TextWriter statusWriter, bool jsonMode)
{
    double filesPerSec = result.Duration.TotalSeconds > 0
        ? result.TotalFiles / result.Duration.TotalSeconds
        : 0;

    statusWriter.WriteLine();
    statusWriter.WriteLine("--- Pipeline Summary ---");
    statusWriter.WriteLine($"  Total files : {result.TotalFiles}");
    statusWriter.WriteLine($"  Succeeded   : {result.Succeeded}");
    statusWriter.WriteLine($"  Failed      : {result.Failed}");
    statusWriter.WriteLine($"  Skipped     : {result.Skipped}");
    statusWriter.WriteLine($"  Duration    : {result.Duration.TotalMilliseconds:F0} ms");
    if (filesPerSec > 0)
    {
        statusWriter.WriteLine($"  Throughput  : {filesPerSec:F1} files/sec");
    }

    statusWriter.WriteLine($"  Dry run     : {result.IsDryRun}");

    // JSON output: structured result to stdout for machine consumption
    if (jsonMode)
    {
        Console.WriteLine(JsonSerializer.Serialize(result, JsonOutputOptions));
    }
}

// Map execution result to CLI exit code: 0 = success, 1 = partial, 2 = total failure.
static int ToExitCode(ExecutionResult result)
{
    if (result.Failed > 0 && result.Succeeded > 0)
    {
        return 1; // partial failure
    }

    if (result.Failed > 0 && result.Succeeded == 0)
    {
        return 2; // total failure
    }

    return 0; // success
}

/// <summary>Partial class to hold cached static members for the top-level Program.</summary>
internal partial class Program
{
    private static readonly JsonSerializerOptions JsonOutputOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
