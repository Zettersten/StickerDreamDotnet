using System.Diagnostics;
using System.Text.RegularExpressions;

namespace StickerDream.Server.Services;

/// <summary>
/// Prints images to USB/Bluetooth thermal printers using CUPS
/// </summary>
public class PrinterService(ILogger<PrinterService> logger) : IPrinterService, IDisposable
{
    private readonly ILogger<PrinterService> _logger = logger;
    private readonly CancellationTokenSource _watchCts = new();
    private Task? _watchTask;

    public async Task<PrintResult> PrintImageAsync(byte[] imageData, PrintOptions? options = null, CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var printRequestId = Guid.NewGuid().ToString("N")[..8];
        options ??= new PrintOptions();

        _logger.LogInformation(
            "Starting print job. PrintRequestId: {PrintRequestId}, ImageSizeBytes: {ImageSize}, Copies: {Copies}, FitToPage: {FitToPage}, Media: {Media}",
            printRequestId, imageData.Length, options.Copies, options.FitToPage, options.Media ?? "default");

        try
        {
            // Get USB printers
            var printers = await GetAvailablePrintersAsync(cancellationToken);
            var usbPrinters = printers.Where(p => p.IsUSB).ToList();

            _logger.LogInformation(
                "Printer discovery completed. PrintRequestId: {PrintRequestId}, TotalPrinters: {TotalCount}, UsbPrinters: {UsbCount}",
                printRequestId, printers.Count, usbPrinters.Count);

            if (usbPrinters.Count == 0)
            {
                _logger.LogError(
                    "No USB printers found. PrintRequestId: {PrintRequestId}, AvailablePrinters: {Printers}",
                    printRequestId, string.Join(", ", printers.Select(p => $"{p.Name}({p.Uri})")));
                throw new InvalidOperationException("No USB printers found");
            }

            // Use default USB printer or first available
            var printer = usbPrinters.FirstOrDefault(p => p.IsDefault) ?? usbPrinters[0];

            _logger.LogInformation(
                "Selected printer for print job. PrintRequestId: {PrintRequestId}, PrinterName: {PrinterName}, PrinterUri: {PrinterUri}, IsDefault: {IsDefault}, Status: {Status}",
                printRequestId, printer.Name, printer.Uri, printer.IsDefault, printer.Status);

            // Create temporary file
            var tempFile = Path.Combine(Path.GetTempPath(), $"print-{printRequestId}-{Guid.NewGuid()}.png");
            
            try
            {
                _logger.LogDebug("Writing image to temporary file. PrintRequestId: {PrintRequestId}, TempFile: {TempFile}", printRequestId, tempFile);
                await File.WriteAllBytesAsync(tempFile, imageData, cancellationToken);

                // Build lp command
                var args = new List<string> { "-d", printer.Name };

                if (options.Copies > 1)
                {
                    args.AddRange(["-n", options.Copies.ToString()]);
                }

                if (options.FitToPage)
                {
                    args.AddRange(["-o", "fit-to-page"]);
                }

                if (!string.IsNullOrEmpty(options.Media))
                {
                    args.AddRange(["-o", $"media={options.Media}"]);
                }

                args.Add(tempFile);

                var command = string.Join(" ", args.Select(a => $"\"{a}\""));
                _logger.LogDebug(
                    "Executing print command. PrintRequestId: {PrintRequestId}, Command: lp {Command}",
                    printRequestId, command);

                var startInfo = new ProcessStartInfo
                {
                    FileName = "lp",
                    Arguments = command,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };

                var processStartTime = System.Diagnostics.Stopwatch.StartNew();
                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    _logger.LogError("Failed to start print process. PrintRequestId: {PrintRequestId}", printRequestId);
                    throw new InvalidOperationException("Failed to start print process");
                }

                await process.WaitForExitAsync(cancellationToken);
                processStartTime.Stop();

                if (process.ExitCode != 0)
                {
                    var error = await process.StandardError.ReadToEndAsync(cancellationToken);
                    _logger.LogError(
                        "Print command failed. PrintRequestId: {PrintRequestId}, ExitCode: {ExitCode}, Error: {Error}, DurationMs: {DurationMs}",
                        printRequestId, process.ExitCode, error, processStartTime.ElapsedMilliseconds);
                    throw new InvalidOperationException($"Print failed: {error}");
                }

                var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
                var jobMatch = Regex.Match(output, @"request id is .+-(\d+)");
                var jobId = jobMatch.Success ? jobMatch.Groups[1].Value : output.Trim();

                stopwatch.Stop();

                _logger.LogInformation(
                    "Print job submitted successfully. PrintRequestId: {PrintRequestId}, PrinterName: {PrinterName}, JobId: {JobId}, TotalDurationMs: {DurationMs}, ProcessDurationMs: {ProcessDurationMs}",
                    printRequestId, printer.Name, jobId, stopwatch.ElapsedMilliseconds, processStartTime.ElapsedMilliseconds);

                return new PrintResult(printer.Name, jobId);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    try
                    {
                        File.Delete(tempFile);
                        _logger.LogDebug("Deleted temporary file. PrintRequestId: {PrintRequestId}, TempFile: {TempFile}", printRequestId, tempFile);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete temporary file. PrintRequestId: {PrintRequestId}, TempFile: {TempFile}", printRequestId, tempFile);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "Print job failed. PrintRequestId: {PrintRequestId}, DurationMs: {DurationMs}",
                printRequestId, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    public async Task<List<PrinterInfo>> GetAvailablePrintersAsync(CancellationToken cancellationToken = default)
    {
        var printers = new List<PrinterInfo>();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        _logger.LogDebug("Starting printer discovery");

        try
        {
            // Get printer list
            var listProcess = new ProcessStartInfo
            {
                FileName = "lpstat",
                Arguments = "-p -d",
                RedirectStandardOutput = true,
                UseShellExecute = false
            };

            using var listProc = Process.Start(listProcess);
            if (listProc == null)
            {
                _logger.LogWarning("Failed to start lpstat process for printer list");
                return printers;
            }

            var listOutput = await listProc.StandardOutput.ReadToEndAsync(cancellationToken);
            await listProc.WaitForExitAsync(cancellationToken);

            if (listProc.ExitCode != 0)
            {
                _logger.LogWarning("lpstat -p -d exited with code {ExitCode}", listProc.ExitCode);
            }

            // Get printer devices
            var deviceProcess = new ProcessStartInfo
            {
                FileName = "lpstat",
                Arguments = "-v",
                RedirectStandardOutput = true,
                UseShellExecute = false
            };

            using var deviceProc = Process.Start(deviceProcess);
            if (deviceProc == null)
            {
                _logger.LogWarning("Failed to start lpstat process for device info");
                return printers;
            }

            var deviceOutput = await deviceProc.StandardOutput.ReadToEndAsync(cancellationToken);
            await deviceProc.WaitForExitAsync(cancellationToken);

            if (deviceProc.ExitCode != 0)
            {
                _logger.LogWarning("lpstat -v exited with code {ExitCode}", deviceProc.ExitCode);
            }

            // Parse default printer
            var defaultMatch = Regex.Match(listOutput, @"system default destination: (.+)");
            var defaultPrinter = defaultMatch.Success ? defaultMatch.Groups[1].Value : string.Empty;

            if (!string.IsNullOrEmpty(defaultPrinter))
            {
                _logger.LogDebug("Default printer detected: {DefaultPrinter}", defaultPrinter);
            }

            // Parse printers
            var printerLines = listOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var deviceLines = deviceOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in printerLines)
            {
                var match = Regex.Match(line, @"printer (.+?) (.+)");
                if (!match.Success) continue;

                var printerName = match.Groups[1].Value;
                var status = match.Groups[2].Value;

                // Find device URI
                var deviceLine = deviceLines.FirstOrDefault(d => d.Contains(printerName));
                var uri = string.Empty;
                var isUSB = false;
                var connectionType = "unknown";

                if (!string.IsNullOrEmpty(deviceLine))
                {
                    var uriMatch = Regex.Match(deviceLine, @"device for (.+?): (.+)");
                    if (uriMatch.Success)
                    {
                        uri = uriMatch.Groups[2].Value;
                        isUSB = uri.Contains("usb", StringComparison.OrdinalIgnoreCase) ||
                                uri.Contains("bluetooth", StringComparison.OrdinalIgnoreCase);
                        
                        if (uri.Contains("usb", StringComparison.OrdinalIgnoreCase))
                            connectionType = "USB";
                        else if (uri.Contains("bluetooth", StringComparison.OrdinalIgnoreCase))
                            connectionType = "Bluetooth";
                        else if (uri.Contains("ipp", StringComparison.OrdinalIgnoreCase))
                            connectionType = "IPP";
                        else if (uri.Contains("socket", StringComparison.OrdinalIgnoreCase))
                            connectionType = "Network";
                    }
                }

                var printerInfo = new PrinterInfo(
                    printerName,
                    uri,
                    status,
                    printerName == defaultPrinter,
                    isUSB
                );

                printers.Add(printerInfo);

                _logger.LogDebug(
                    "Discovered printer. Name: {PrinterName}, Uri: {PrinterUri}, ConnectionType: {ConnectionType}, IsUSB: {IsUSB}, IsDefault: {IsDefault}, Status: {Status}",
                    printerName, uri, connectionType, isUSB, printerName == defaultPrinter, status);
            }

            stopwatch.Stop();

            var usbCount = printers.Count(p => p.IsUSB);
            var bluetoothCount = printers.Count(p => p.Uri.Contains("bluetooth", StringComparison.OrdinalIgnoreCase));
            var defaultCount = printers.Count(p => p.IsDefault);

            _logger.LogInformation(
                "Printer discovery completed. TotalPrinters: {TotalCount}, UsbPrinters: {UsbCount}, BluetoothPrinters: {BluetoothCount}, DefaultPrinters: {DefaultCount}, DurationMs: {DurationMs}",
                printers.Count, usbCount, bluetoothCount, defaultCount, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Failed to get printers. DurationMs: {DurationMs}", stopwatch.ElapsedMilliseconds);
        }

        return printers;
    }

    public Task StartWatchingPrintersAsync(CancellationToken cancellationToken = default)
    {
        if (_watchTask != null)
        {
            _logger.LogDebug("Printer watcher already started");
            return _watchTask;
        }

        _logger.LogInformation("Starting printer watcher service");

        _watchTask = Task.Run(async () =>
        {
            _logger.LogInformation("Printer watcher service started");
            var checkCount = 0;

            while (!_watchCts.Token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(1000, _watchCts.Token);
                    checkCount++;

                    var printers = await GetAvailablePrintersAsync(_watchCts.Token);
                    var usbPrinters = printers.Where(p => p.IsUSB).ToList();

                    foreach (var printer in usbPrinters)
                    {
                        var isDisabled = printer.Status.Contains("disabled", StringComparison.OrdinalIgnoreCase);
                        var isPaused = printer.Status.Contains("paused", StringComparison.OrdinalIgnoreCase);

                        if (isDisabled || isPaused)
                        {
                            _logger.LogWarning(
                                "Printer requires attention. PrinterName: {PrinterName}, Status: {Status}, IsDisabled: {IsDisabled}, IsPaused: {IsPaused}, CheckCount: {CheckCount}",
                                printer.Name, printer.Status, isDisabled, isPaused, checkCount);

                            await EnablePrinterAsync(printer.Name, _watchCts.Token);
                        }
                    }

                    // Log periodic status every 60 checks (approximately every minute)
                    if (checkCount % 60 == 0)
                    {
                        var healthyPrinters = usbPrinters.Count(p => 
                            !p.Status.Contains("disabled", StringComparison.OrdinalIgnoreCase) &&
                            !p.Status.Contains("paused", StringComparison.OrdinalIgnoreCase));
                        
                        _logger.LogInformation(
                            "Printer watcher status check. TotalUsbPrinters: {TotalCount}, HealthyPrinters: {HealthyCount}, CheckCount: {CheckCount}",
                            usbPrinters.Count, healthyPrinters, checkCount);
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Printer watcher service stopping (cancellation requested)");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in printer watcher service. CheckCount: {CheckCount}", checkCount);
                }
            }

            _logger.LogInformation("Printer watcher service stopped");
        }, _watchCts.Token);

        return _watchTask;
    }

    private async Task EnablePrinterAsync(string printerName, CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation("Attempting to enable printer. PrinterName: {PrinterName}", printerName);

        try
        {
            var enableProcess = new ProcessStartInfo
            {
                FileName = "cupsenable",
                Arguments = $"\"{printerName}\"",
                RedirectStandardError = true,
                UseShellExecute = false
            };

            using var proc = Process.Start(enableProcess);
            if (proc != null)
            {
                await proc.WaitForExitAsync(cancellationToken);
                
                if (proc.ExitCode != 0)
                {
                    var error = await proc.StandardError.ReadToEndAsync(cancellationToken);
                    _logger.LogWarning(
                        "cupsenable exited with error. PrinterName: {PrinterName}, ExitCode: {ExitCode}, Error: {Error}",
                        printerName, proc.ExitCode, error);
                }
                else
                {
                    _logger.LogDebug("cupsenable succeeded. PrinterName: {PrinterName}", printerName);
                }
            }

            var acceptProcess = new ProcessStartInfo
            {
                FileName = "cupsaccept",
                Arguments = $"\"{printerName}\"",
                RedirectStandardError = true,
                UseShellExecute = false
            };

            using var acceptProc = Process.Start(acceptProcess);
            if (acceptProc != null)
            {
                await acceptProc.WaitForExitAsync(cancellationToken);
                
                if (acceptProc.ExitCode != 0)
                {
                    var error = await acceptProc.StandardError.ReadToEndAsync(cancellationToken);
                    _logger.LogWarning(
                        "cupsaccept exited with error. PrinterName: {PrinterName}, ExitCode: {ExitCode}, Error: {Error}",
                        printerName, acceptProc.ExitCode, error);
                }
                else
                {
                    _logger.LogDebug("cupsaccept succeeded. PrinterName: {PrinterName}", printerName);
                }
            }

            stopwatch.Stop();
            _logger.LogInformation(
                "Successfully enabled printer. PrinterName: {PrinterName}, DurationMs: {DurationMs}",
                printerName, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(ex,
                "Failed to enable printer. PrinterName: {PrinterName}, DurationMs: {DurationMs}",
                printerName, stopwatch.ElapsedMilliseconds);
        }
    }

    public void Dispose()
    {
        _watchCts.Cancel();
        _watchTask?.Wait(TimeSpan.FromSeconds(5));
        _watchCts.Dispose();
    }
}
