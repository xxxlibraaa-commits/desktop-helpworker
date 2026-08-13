using System.Windows;
using System.Windows.Threading;

namespace FloatMate;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        var previewArgument = e.Args.FirstOrDefault(argument =>
            argument.StartsWith("--capture-preview=", StringComparison.OrdinalIgnoreCase));
        var exportArgument = e.Args.FirstOrDefault(argument =>
            argument.StartsWith("--export-xlsm=", StringComparison.OrdinalIgnoreCase));
        if (previewArgument is not null || exportArgument is not null) window.EnablePreviewMode();
        window.Show();

        if (previewArgument is not null)
        {
            var outputPath = previewArgument[(previewArgument.IndexOf('=') + 1)..].Trim('"');
            var expandedPreview = e.Args.Any(argument => argument.Equals("--capture-expanded", StringComparison.OrdinalIgnoreCase));
            var pageArgument = e.Args.FirstOrDefault(argument => argument.StartsWith("--capture-page=", StringComparison.OrdinalIgnoreCase));
            var previewPage = pageArgument is null ? "today" : pageArgument[(pageArgument.IndexOf('=') + 1)..];
            window.Dispatcher.InvokeAsync(async () =>
            {
                window.PreparePreview(expandedPreview, previewPage);
                await Task.Delay(700);
                window.CapturePreview(outputPath);
                window.ExitAfterPreview();
            }, DispatcherPriority.ApplicationIdle);
        }
        else if (exportArgument is not null)
        {
            var outputPath = exportArgument[(exportArgument.IndexOf('=') + 1)..].Trim('"');
            window.Dispatcher.InvokeAsync(() =>
            {
                window.ExportTodayWorkbook(outputPath);
                window.ExitAfterPreview();
            }, DispatcherPriority.ApplicationIdle);
        }
    }
}
