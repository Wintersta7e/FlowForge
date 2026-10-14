using Avalonia;
using Avalonia.Headless;
using FlowForge.Tests.Visual;

[assembly: AvaloniaTestApplication(typeof(HeadlessTestApp))]

namespace FlowForge.Tests.Visual;

/// <summary>
/// Boots the app's own builder, switched to the headless platform with Skia drawing,
/// so tests render the real themed UI in-process.
/// </summary>
public static class HeadlessTestApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        FlowForge.UI.Program.BuildAvaloniaApp()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
