using Avalonia.Media;

namespace FlowForge.UI.ViewModels;

/// <summary>An empty-canvas card that loads one library template.</summary>
public sealed record TemplateCardViewModel(string Id, string Code, string Title, string Description, int StationCount, IBrush Accent);
