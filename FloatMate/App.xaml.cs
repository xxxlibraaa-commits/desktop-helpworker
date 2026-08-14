using System.Windows;
using System.Windows.Threading;
using FloatMate.Services;

namespace FloatMate;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var planImportArgument = e.Args.FirstOrDefault(argument =>
            argument.StartsWith("--import-plan=", StringComparison.OrdinalIgnoreCase));
        var planExportArgument = e.Args.FirstOrDefault(argument =>
            argument.StartsWith("--export-plan=", StringComparison.OrdinalIgnoreCase));
        if (planImportArgument is not null && planExportArgument is not null)
        {
            var inputPath = planImportArgument[(planImportArgument.IndexOf('=') + 1)..].Trim('"');
            var outputPath = planExportArgument[(planExportArgument.IndexOf('=') + 1)..].Trim('"');
            var service = new PlanWorkbookService();
            service.Export(outputPath, service.Import(inputPath));
            Shutdown();
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        var previewArgument = e.Args.FirstOrDefault(argument =>
            argument.StartsWith("--capture-preview=", StringComparison.OrdinalIgnoreCase));
        var exportArgument = e.Args.FirstOrDefault(argument =>
            argument.StartsWith("--export-xlsm=", StringComparison.OrdinalIgnoreCase));
        var workDocxExportArgument = e.Args.FirstOrDefault(argument =>
            argument.StartsWith("--export-work-docx=", StringComparison.OrdinalIgnoreCase));
        var healthExportArgument = e.Args.FirstOrDefault(argument =>
            argument.StartsWith("--export-health-xlsx=", StringComparison.OrdinalIgnoreCase));
        if (previewArgument is not null || exportArgument is not null || workDocxExportArgument is not null || healthExportArgument is not null) window.EnablePreviewMode();
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
                window.ExportWorkWorkbook(outputPath);
                window.ExitAfterPreview();
            }, DispatcherPriority.ApplicationIdle);
        }
        else if (workDocxExportArgument is not null)
        {
            var outputPath = workDocxExportArgument[(workDocxExportArgument.IndexOf('=') + 1)..].Trim('"');
            window.Dispatcher.InvokeAsync(() =>
            {
                window.ExportWorkDocument(outputPath);
                window.ExitAfterPreview();
            }, DispatcherPriority.ApplicationIdle);
        }
        else if (healthExportArgument is not null)
        {
            var outputPath = healthExportArgument[(healthExportArgument.IndexOf('=') + 1)..].Trim('"');
            window.Dispatcher.InvokeAsync(() =>
            {
                window.ExportHealthWorkbook(outputPath);
                window.ExitAfterPreview();
            }, DispatcherPriority.ApplicationIdle);
        }
    }
}
