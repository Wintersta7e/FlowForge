using System.CommandLine;
using System.Text.Json;
using FlowForge.Core.Execution;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Base;
using FlowForge.Core.Pipeline;
using Serilog;
using Serilog.Events;

var rootCommand = new RootCommand("FlowForge — node-based file processing pipeline runner");

var pipelineArgument = new Argument<FileInfo>(
    name: "pipeline",
    description: "Path to the .ffpipe pipeline file");

var inputOption = new Option<DirectoryInfo?>(
    name: "--input",
    description: "Override the first FolderInput node's source folder");

var outputOption = new Option<DirectoryInfo?>(
    name: "--output",
    description: "Override the first FolderOutput node's destination folder");

var dryRunOption = new Option<bool>(
    name: "--dry-run",
    description: "Simulate the pipeline without writing any files");

var verboseOption = new Option<bool>(
    name: "--verbose",
    description: "Print per-file log entries to stdout");

var runCommand = new Command("run", "Execute a FlowForge pipeline")
{
    pipelineArgument,
    inputOption,
    outputOption,
    dryRunOption,
    verboseOption
};

rootCommand.AddCommand(runCommand);

runCommand.SetHandler(async (FileInfo pipelineFile, DirectoryInfo? inputDir, DirectoryInfo? outputDir, bool dryRun, bool verbose) =>
{
    int exitCode = await RunPipelineAsync(pipelineFile, inputDir, outputDir, dryRun, verbose);
    Environment.ExitCode = exitCode;
}, pipelineArgument, inputOption, outputOption, dryRunOption, verboseOption);

return await rootCommand.InvokeAsync(args);

static async Task<int> RunPipelineAsync(
    FileInfo pipelineFile,
    DirectoryInfo? inputDir,
    DirectoryInfo? outputDir,
    bool dryRun,
    bool verbose)
{
    // Configure Serilog
    LogEventLevel minimumLevel = verbose ? LogEventLevel.Information : LogEventLevel.Warning;
    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Is(minimumLevel)
        .WriteTo.Console(
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
        .CreateLogger();

    ILogger logger = Log.Logger;

    // Wire up cancellation via Ctrl+C
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
        Console.Error.WriteLine("Cancellation requested...");
    };

    try
    {
        // Validate pipeline file exists
        if (!pipelineFile.Exists)
        {
            Console.Error.WriteLine($"Pipeline file not found: '{pipelineFile.FullName}'");
            return 2;
        }

        // Load pipeline
        string fullPath = pipelineFile.FullName;
        Console.WriteLine($"Loading pipeline: {fullPath}");

        PipelineGraph graph = await PipelineSerializer.LoadAsync(fullPath, cts.Token);
        Console.WriteLine($"Pipeline '{graph.Name}' loaded ({graph.Nodes.Count} nodes, {graph.Connections.Count} connections)");

        // Override input path if provided
        if (inputDir is not null)
        {
            NodeDefinition? folderInputNode = graph.Nodes.Find(n =>
                n.TypeKey.Equals("FolderInput", StringComparison.Ordinal));

            if (folderInputNode is null)
            {
                Console.Error.WriteLine("No FolderInput node found in pipeline to override.");
                return 2;
            }

            folderInputNode.Config["path"] = JsonSerializer.SerializeToElement(inputDir.FullName);
            Console.WriteLine($"Input override: {inputDir.FullName}");
        }

        // Override output path if provided
        if (outputDir is not null)
        {
            NodeDefinition? folderOutputNode = graph.Nodes.Find(n =>
                n.TypeKey.Equals("FolderOutput", StringComparison.Ordinal));

            if (folderOutputNode is null)
            {
                Console.Error.WriteLine("No FolderOutput node found in pipeline to override.");
                return 2;
            }

            folderOutputNode.Config["path"] = JsonSerializer.SerializeToElement(outputDir.FullName);
            Console.WriteLine($"Output override: {outputDir.FullName}");
        }

        if (dryRun)
        {
            Console.WriteLine("Mode: DRY RUN (no files will be written)");
        }

        Console.WriteLine();

        // Create registry and runner
        NodeRegistry registry = NodeRegistry.CreateDefault();
        var runner = new PipelineRunner(registry, logger);

        // Progress reporter
        IProgress<FileJob> progress = new Progress<FileJob>(job =>
        {
            string statusIcon = job.Status switch
            {
                FileJobStatus.Succeeded => "[OK]",
                FileJobStatus.Failed => "[FAIL]",
                FileJobStatus.Skipped => "[SKIP]",
                _ => "[??]"
            };

            Console.WriteLine($"  {statusIcon} {Path.GetFileName(job.OriginalPath)}");

            if (verbose)
            {
                foreach (string logEntry in job.NodeLog)
                {
                    Console.WriteLine($"        {logEntry}");
                }
            }

            if (job.Status == FileJobStatus.Failed && job.ErrorMessage is not null)
            {
                Console.Error.WriteLine($"        Error: {job.ErrorMessage}");
            }
        });

        // Run the pipeline
        ExecutionResult result = await runner.RunAsync(graph, dryRun, progress, cts.Token);

        // Print summary
        Console.WriteLine();
        Console.WriteLine("--- Pipeline Summary ---");
        Console.WriteLine($"  Total files : {result.TotalFiles}");
        Console.WriteLine($"  Succeeded   : {result.Succeeded}");
        Console.WriteLine($"  Failed      : {result.Failed}");
        Console.WriteLine($"  Skipped     : {result.Skipped}");
        Console.WriteLine($"  Duration    : {result.Duration.TotalMilliseconds:F0} ms");
        Console.WriteLine($"  Dry run     : {result.IsDryRun}");

        // Determine exit code
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
        Console.Error.WriteLine($"Pipeline error: {ex.Message}");
        return 2;
    }
    finally
    {
        await Log.CloseAndFlushAsync();
    }
}
