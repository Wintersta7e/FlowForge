using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowForge.Core.Execution;
using FlowForge.Core.Models;
using FlowForge.Core.Pipeline;
using FlowForge.UI.Services;
using Serilog;

namespace FlowForge.UI.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly NodeRegistry _registry;
    private readonly IDialogService _dialogService;
    private readonly ILogger _logger;

    public NodeRegistry Registry => _registry;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private string _title = "FlowForge - Untitled";

    [ObservableProperty]
    private string? _currentFilePath;

    [ObservableProperty]
    private bool _isDirty;

    public EditorViewModel Editor { get; }
    public NodeLibraryViewModel NodeLibrary { get; }
    public PropertiesViewModel Properties { get; }
    public ExecutionLogViewModel ExecutionLog { get; }

    public MainWindowViewModel()
    {
        _registry = NodeRegistry.CreateDefault();
        _dialogService = new DialogService();
        _logger = Log.Logger;

        Editor = new EditorViewModel();
        NodeLibrary = new NodeLibraryViewModel();
        Properties = new PropertiesViewModel();
        ExecutionLog = new ExecutionLogViewModel();

        NodeLibrary.Initialize(_registry);

        // Wire selection changes to properties panel
        Editor.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(EditorViewModel.SelectedNode))
            {
                Properties.LoadNode(Editor.SelectedNode, _registry);
            }
        };
    }

    [RelayCommand]
    private void New()
    {
        Editor.Nodes.Clear();
        Editor.Connections.Clear();
        Editor.SelectedNode = null;
        CurrentFilePath = null;
        IsDirty = false;
        Title = "FlowForge - Untitled";
        ExecutionLog.Clear();
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        string? path = await _dialogService.OpenFileAsync(
            "Open Pipeline",
            "Pipeline Files|*.ffpipe|All Files|*.*");

        if (path is null)
        {
            return;
        }

        try
        {
            PipelineGraph graph = await PipelineSerializer.LoadAsync(path);
            Editor.LoadGraph(graph, _registry);
            CurrentFilePath = path;
            IsDirty = false;
            Title = $"FlowForge - {Path.GetFileNameWithoutExtension(path)}";
        }
        catch (PipelineLoadException ex)
        {
            _logger.Error(ex, "Failed to load pipeline from {Path}", path);
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (CurrentFilePath is null)
        {
            await SaveAsInternalAsync();
            return;
        }

        await SaveToFileAsync(CurrentFilePath);
    }

    [RelayCommand]
    private async Task SaveAsAsync()
    {
        await SaveAsInternalAsync();
    }

    private async Task SaveAsInternalAsync()
    {
        string? path = await _dialogService.SaveFileAsync(
            "Save Pipeline",
            "Pipeline Files|*.ffpipe",
            "Untitled.ffpipe");

        if (path is null)
        {
            return;
        }

        await SaveToFileAsync(path);
    }

    private async Task SaveToFileAsync(string path)
    {
        try
        {
            PipelineGraph graph = Editor.BuildGraph();
            await PipelineSerializer.SaveAsync(graph, path);
            CurrentFilePath = path;
            IsDirty = false;
            Title = $"FlowForge - {Path.GetFileNameWithoutExtension(path)}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Error(ex, "Failed to save pipeline to {Path}", path);
        }
    }

    [RelayCommand]
    private async Task RunAsync()
    {
        await ExecutePipelineAsync(dryRun: false);
    }

    [RelayCommand]
    private async Task DryRunAsync()
    {
        await ExecutePipelineAsync(dryRun: true);
    }

    private async Task ExecutePipelineAsync(bool dryRun)
    {
        if (ExecutionLog.IsRunning)
        {
            return;
        }

        ExecutionLog.Clear();
        ExecutionLog.IsRunning = true;

        _cts = new CancellationTokenSource();

        try
        {
            PipelineGraph graph = Editor.BuildGraph();
            PipelineRunner runner = new(_registry, _logger);

            Progress<FileJob> progress = new(job =>
            {
                ExecutionLog.ReportProgress(job);
            });

            ExecutionResult result = await runner.RunAsync(graph, dryRun, progress, _cts.Token);

            // Update total from actual result (corrects any estimate)
            ExecutionLog.TotalFiles = result.TotalFiles;
            int processed = ExecutionLog.Succeeded + ExecutionLog.Failed + ExecutionLog.Skipped;
            ExecutionLog.Progress = result.TotalFiles > 0
                ? (double)processed / result.TotalFiles * 100.0
                : 100.0;
            ExecutionLog.Summary = dryRun
                ? $"Dry run complete: {result.TotalFiles} files, {result.Succeeded} OK, {result.Failed} failed, {result.Skipped} skipped ({result.Duration.TotalMilliseconds:F0}ms)"
                : $"Run complete: {result.TotalFiles} files, {result.Succeeded} OK, {result.Failed} failed, {result.Skipped} skipped ({result.Duration.TotalMilliseconds:F0}ms)";
        }
        catch (OperationCanceledException)
        {
            ExecutionLog.Summary = "Execution cancelled.";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Pipeline execution failed");
            ExecutionLog.Summary = $"Execution failed: {ex.Message}";
        }
        finally
        {
            ExecutionLog.IsRunning = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
    }
}
