using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowForge.Core.Execution;
using FlowForge.Core.Pipeline;
using FlowForge.Core.Pipeline.Templates;
using FlowForge.Core.Settings;
using FlowForge.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FlowForge.UI.ViewModels;

[System.Diagnostics.CodeAnalysis.SuppressMessage("IDisposable", "CA1001", Justification = "CTS lifetime managed by MVVM lifecycle, not IDisposable")]
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly NodeRegistry _registry;
    private readonly IDialogService _dialogService;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly AppSettingsManager _settingsManager;
    private readonly IServiceProvider _serviceProvider;
    private readonly TaskCompletionSource _initComplete = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private AppSettings _appSettings = new();

    public NodeRegistry Registry => _registry;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private string _title = "FlowForge - Untitled";

    [ObservableProperty]
    private string? _currentFilePath;

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private bool _isDarkTheme = true;

    [ObservableProperty]
    private string _themeIcon = "\u263E";

    public IReadOnlyList<RecentPipelineItem> RecentPipelineItems { get; private set; } = new List<RecentPipelineItem>();

    public EditorViewModel Editor { get; }
    public NodeLibraryViewModel NodeLibrary { get; }
    public PropertiesViewModel Properties { get; }
    public ExecutionLogViewModel ExecutionLog { get; }

    public MainWindowViewModel(
        ILogger<MainWindowViewModel> logger,
        AppSettingsManager settingsManager,
        NodeRegistry registry,
        EditorViewModel editor,
        IDialogService dialogService,
        IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(settingsManager);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(dialogService);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        _registry = registry;
        _dialogService = dialogService;
        _logger = logger;
        _settingsManager = settingsManager;
        _serviceProvider = serviceProvider;

        Editor = editor;
        NodeLibrary = new NodeLibraryViewModel();
        Properties = new PropertiesViewModel();
        ExecutionLog = new ExecutionLogViewModel();

        NodeLibrary.Initialize(_registry);

        // Wire selection changes to properties panel
        Editor.PropertyChanged += OnEditorPropertyChanged;

        // Refresh properties panel on undo/redo so fields read fresh config values
        Editor.UndoRedo.StateChanged += OnUndoRedoStateChanged;

        // Track all graph changes (structural + config edits) for IsDirty
        Editor.GraphChanged += OnEditorGraphChanged;
    }

    private void OnEditorPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (string.Equals(args.PropertyName, nameof(EditorViewModel.SelectedNode), StringComparison.Ordinal))
        {
            RefreshPropertiesPanel();
        }
    }

    private void OnUndoRedoStateChanged(object? sender, EventArgs e)
    {
        RefreshPropertiesPanel();
    }

    private void OnEditorGraphChanged(object? sender, EventArgs e)
    {
        IsDirty = true;
    }

    private void RefreshPropertiesPanel()
    {
        // PushExecuted (not Execute) because the MVVM binding already mutated the config dictionary.
        // PushOrCoalesce merges repeated keystrokes on the same field into a single undo entry.
        Properties.LoadNode(Editor.SelectedNode, _registry, cmd =>
        {
            if (cmd is UndoRedo.Commands.ChangeConfigCommand configCmd)
            {
                Editor.UndoRedo.PushOrCoalesce(cmd, prev =>
                    prev is UndoRedo.Commands.ChangeConfigCommand prevConfig
                    && string.Equals(prevConfig.ConfigKey, configCmd.ConfigKey, StringComparison.Ordinal));
            }
            else
            {
                Editor.UndoRedo.PushExecuted(cmd);
            }

            Editor.RaiseGraphChanged();
        });
    }

    public async Task InitializeAsync()
    {
        try
        {
            _appSettings = await _settingsManager.LoadAsync();
            RefreshRecentPipelines();
        }
        finally
        {
            _initComplete.TrySetResult();
        }
    }

    private void RefreshRecentPipelines()
    {
        RecentPipelineItems = _appSettings.RecentPipelines
            .Where(p => p.EndsWith(".ffpipe", StringComparison.OrdinalIgnoreCase) && File.Exists(p))
            .Select(p => new RecentPipelineItem(Path.GetFileName(p), p))
            .ToList();
        OnPropertyChanged(nameof(RecentPipelineItems));
    }

    private static void RemoveRecentPipeline(IList<string> list, string path)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (string.Equals(list[i], path, StringComparison.OrdinalIgnoreCase))
            {
                list.RemoveAt(i);
            }
        }
    }

    private async Task TrackRecentPipelineAsync(string path)
    {
        await _initComplete.Task;

        if (!path.EndsWith(".ffpipe", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _appSettings.AddRecentPipeline(path);
        RefreshRecentPipelines();
        await _settingsManager.SaveAsync(_appSettings);
    }

    [RelayCommand]
    private async Task OpenRecentAsync(string path)
    {
        try
        {
            PipelineGraph graph = await PipelineSerializer.LoadAsync(path, CancellationToken.None);
            int droppedConnections = Editor.LoadGraph(graph, _registry);
            CurrentFilePath = path;
            IsDirty = false;
            Title = $"FlowForge - {Path.GetFileNameWithoutExtension(path)}";
            await TrackRecentPipelineAsync(path);

            if (droppedConnections > 0)
            {
                ExecutionLog.Summary = $"Loaded with {droppedConnections} dropped connection(s).";
            }
        }
        catch (FileNotFoundException)
        {
            RemoveRecentPipeline(_appSettings.RecentPipelines, path);
            RefreshRecentPipelines();
            await _settingsManager.SaveAsync(_appSettings);
            ExecutionLog.Summary = $"File not found: {Path.GetFileName(path)}";
        }
        catch (DirectoryNotFoundException)
        {
            RemoveRecentPipeline(_appSettings.RecentPipelines, path);
            RefreshRecentPipelines();
            await _settingsManager.SaveAsync(_appSettings);
            ExecutionLog.Summary = $"File not found: {Path.GetFileName(path)}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to load pipeline from {Path}", path);
            ExecutionLog.Summary = $"Failed to open: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ClearRecentAsync()
    {
        _appSettings.ClearRecentPipelines();
        RefreshRecentPipelines();
        await _settingsManager.SaveAsync(_appSettings);
    }

    [RelayCommand]
    private void New()
    {
        Editor.ClearAll();
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
            PipelineGraph graph = await PipelineSerializer.LoadAsync(path, CancellationToken.None);
            int droppedConnections = Editor.LoadGraph(graph, _registry);
            CurrentFilePath = path;
            IsDirty = false;
            Title = $"FlowForge - {Path.GetFileNameWithoutExtension(path)}";
            await TrackRecentPipelineAsync(path);

            if (droppedConnections > 0)
            {
                ExecutionLog.Summary = $"Loaded with {droppedConnections} dropped connection(s).";
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to load pipeline from {Path}", path);
            ExecutionLog.Summary = $"Failed to open: {ex.Message}";
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
            await TrackRecentPipelineAsync(path);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to save pipeline to {Path}", path);
            ExecutionLog.Summary = $"Save failed: {ex.Message}";
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

            // Resolve fresh PipelineRunner per execution (transient lifetime)
            PipelineRunner runner = _serviceProvider.GetRequiredService<PipelineRunner>();

            Progress<PipelineProgressEvent> progress = new(evt =>
            {
                ExecutionLog.ReportProgressEvent(evt);
            });

            ExecutionResult result = await runner.RunAsync(graph, dryRun, progress, _cts.Token);

            // Summary with throughput
            double filesPerSec = result.Duration.TotalSeconds > 0
                ? result.TotalFiles / result.Duration.TotalSeconds
                : 0;
            string throughput = filesPerSec > 0 ? $", {filesPerSec:F1} files/sec" : string.Empty;
            ExecutionLog.Summary = dryRun
                ? $"Preview complete: {result.TotalFiles} files, {result.Succeeded} OK, {result.Failed} failed, {result.Skipped} skipped ({result.Duration.TotalMilliseconds:F0}ms{throughput})"
                : $"Run complete: {result.TotalFiles} files, {result.Succeeded} OK, {result.Failed} failed, {result.Skipped} skipped ({result.Duration.TotalMilliseconds:F0}ms{throughput})";
        }
        catch (OperationCanceledException)
        {
            ExecutionLog.Summary = "Execution cancelled.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pipeline execution failed");
            ExecutionLog.Summary = $"Execution failed: {ex.Message}";
        }
        finally
        {
            ExecutionLog.IsRunning = false;
            Interlocked.Exchange(ref _cts, null)?.Dispose();
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        CancellationTokenSource? cts = _cts;
        cts?.Cancel();
    }

    [RelayCommand]
    private void Undo()
    {
        Editor.Undo();
    }

    [RelayCommand]
    private void Redo()
    {
        Editor.Redo();
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        if (Application.Current is null)
        {
            return;
        }

        IsDarkTheme = !IsDarkTheme;
        Application.Current.RequestedThemeVariant = IsDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
        ThemeIcon = IsDarkTheme ? "\u263E" : "\u2600";
        NodeLibrary.RefreshBrushes();
    }

    [RelayCommand]
    private void LoadTemplate(string templateId)
    {
        try
        {
            PipelineGraph graph = PipelineTemplateLibrary.CreateFromTemplate(templateId);

            // Spread nodes horizontally so they don't stack at (0,0)
            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                graph.Nodes[i].Position = new CanvasPosition(100 + (i * 250), 200);
            }

            Editor.LoadGraph(graph, _registry);
            CurrentFilePath = null;
            IsDirty = true;
            Title = $"FlowForge - {graph.Name ?? "Untitled"}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to load template {TemplateId}", templateId);
            ExecutionLog.Summary = $"Template load failed: {ex.Message}";
        }
    }
}
