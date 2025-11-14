namespace StickerDream.Server.Services;

/// <summary>
/// Background service that watches for printer status changes and automatically enables disabled/paused printers
/// </summary>
public sealed class PrinterWatcherService(
    IServiceProvider serviceProvider,
    ILogger<PrinterWatcherService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Printer watcher service started");

        await using var scope = serviceProvider.CreateAsyncScope();
        var printerService = scope.ServiceProvider.GetRequiredService<IPrinterService>();
        
        var checkCount = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                checkCount++;

                var printers = await printerService.GetAvailablePrintersAsync(stoppingToken);
                var usbPrinters = printers.Where(p => p.IsUSB).ToList();

                foreach (var printer in usbPrinters)
                {
                    var isDisabled = printer.Status.Contains("disabled", StringComparison.OrdinalIgnoreCase);
                    var isPaused = printer.Status.Contains("paused", StringComparison.OrdinalIgnoreCase);

                    if (isDisabled || isPaused)
                    {
                        logger.LogWarning(
                            "Printer requires attention. PrinterName: {PrinterName}, Status: {Status}, IsDisabled: {IsDisabled}, IsPaused: {IsPaused}, CheckCount: {CheckCount}",
                            printer.Name, printer.Status, isDisabled, isPaused, checkCount);

                        await EnablePrinterAsync(printer.Name, stoppingToken);
                    }
                }

                // Log periodic status every 60 checks (approximately every minute)
                if (checkCount % 60 == 0)
                {
                    var healthyPrinters = usbPrinters.Count(p => 
                        !p.Status.Contains("disabled", StringComparison.OrdinalIgnoreCase) &&
                        !p.Status.Contains("paused", StringComparison.OrdinalIgnoreCase));
                    
                    logger.LogInformation(
                        "Printer watcher status check. TotalUsbPrinters: {TotalCount}, HealthyPrinters: {HealthyCount}, CheckCount: {CheckCount}",
                        usbPrinters.Count, healthyPrinters, checkCount);
                }
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("Printer watcher service stopping (cancellation requested)");
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in printer watcher service. CheckCount: {CheckCount}", checkCount);
            }
        }

        logger.LogInformation("Printer watcher service stopped");
    }

    private async Task EnablePrinterAsync(string printerName, CancellationToken cancellationToken)
    {
        var startTime = TimeProvider.System.GetTimestamp();
        logger.LogInformation("Attempting to enable printer. PrinterName: {PrinterName}", printerName);

        try
        {
            await using var scope = serviceProvider.CreateAsyncScope();
            var printerService = scope.ServiceProvider.GetRequiredService<IPrinterService>();
            
            var enableProcess = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cupsenable",
                Arguments = $"\"{printerName}\"",
                RedirectStandardError = true,
                UseShellExecute = false
            };

            using var proc = System.Diagnostics.Process.Start(enableProcess);
            if (proc != null)
            {
                await proc.WaitForExitAsync(cancellationToken);
                
                if (proc.ExitCode != 0)
                {
                    var error = await proc.StandardError.ReadToEndAsync(cancellationToken);
                    logger.LogWarning(
                        "cupsenable exited with error. PrinterName: {PrinterName}, ExitCode: {ExitCode}, Error: {Error}",
                        printerName, proc.ExitCode, error);
                }
                else
                {
                    logger.LogDebug("cupsenable succeeded. PrinterName: {PrinterName}", printerName);
                }
            }

            var acceptProcess = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cupsaccept",
                Arguments = $"\"{printerName}\"",
                RedirectStandardError = true,
                UseShellExecute = false
            };

            using var acceptProc = System.Diagnostics.Process.Start(acceptProcess);
            if (acceptProc != null)
            {
                await acceptProc.WaitForExitAsync(cancellationToken);
                
                if (acceptProc.ExitCode != 0)
                {
                    var error = await acceptProc.StandardError.ReadToEndAsync(cancellationToken);
                    logger.LogWarning(
                        "cupsaccept exited with error. PrinterName: {PrinterName}, ExitCode: {ExitCode}, Error: {Error}",
                        printerName, acceptProc.ExitCode, error);
                }
                else
                {
                    logger.LogDebug("cupsaccept succeeded. PrinterName: {PrinterName}", printerName);
                }
            }

            var duration = TimeProvider.System.GetElapsedTime(startTime);
            logger.LogInformation(
                "Successfully enabled printer. PrinterName: {PrinterName}, DurationMs: {DurationMs}",
                printerName, duration.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            var duration = TimeProvider.System.GetElapsedTime(startTime);
            logger.LogWarning(ex,
                "Failed to enable printer. PrinterName: {PrinterName}, DurationMs: {DurationMs}",
                printerName, duration.TotalMilliseconds);
        }
    }
}
