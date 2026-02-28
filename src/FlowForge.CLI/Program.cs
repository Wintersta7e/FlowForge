using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlowForge.Core.Execution;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Base;
using FlowForge.Core.Pipeline;
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

    int exitCode = await RunPipelineAsync(pipelineFile, inputDir, outputDir, dryRun, verbose, format, cancellationToken);
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
    // Configure Serilog
    LogEventLevel minimumLevel = verbose ? LogEventLevel.Information : LogEventLevel.Warning;
    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Is(minimumLevel)
        .WriteTo.Console(
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
            formatProvider: CultureInfo.InvariantCulture)
        .CreateLogger();

    ILogger logger = Log.Logger;
    bool jsonMode = format.Equals("json", StringComparison.OrdinalIgnoreCase);
    TextWriter statusWriter = jsonMode ? Console.Error : Console.Out;

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
        statusWriter.WriteLine($"Loading pipeline: {fullPath}");

        PipelineGraph graph = await PipelineSerializer.LoadAsync(fullPath, cancellationToken);
        statusWriter.WriteLine($"Pipeline '{graph.Name}' loaded ({graph.Nodes.Count} nodes, {graph.Connections.Count} connections)");

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
            statusWriter.WriteLine($"Input override: {inputDir.FullName}");
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
            statusWriter.WriteLine($"Output override: {outputDir.FullName}");
        }

        if (dryRun)
        {
            statusWriter.WriteLine("Mode: DRY RUN (no files will be written)");
        }

        statusWriter.WriteLine();

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
        });

        // Run the pipeline
        ExecutionResult result = await runner.RunAsync(graph, dryRun, progress, cancellationToken);

        // Print summary
        statusWriter.WriteLine();
        statusWriter.WriteLine("--- Pipeline Summary ---");
        statusWriter.WriteLine($"  Total files : {result.TotalFiles}");
        statusWriter.WriteLine($"  Succeeded   : {result.Succeeded}");
        statusWriter.WriteLine($"  Failed      : {result.Failed}");
        statusWriter.WriteLine($"  Skipped     : {result.Skipped}");
        statusWriter.WriteLine($"  Duration    : {result.Duration.TotalMilliseconds:F0} ms");
        statusWriter.WriteLine($"  Dry run     : {result.IsDryRun}");

        // JSON output: structured result to stdout for machine consumption
        if (jsonMode)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOutputOptions));
        }

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
