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
        options ??= new PrintOptions();

        // Get USB printers
        var printers = await GetAvailablePrintersAsync(cancellationToken);
        var usbPrinters = printers.Where(p => p.IsUSB).ToList();

        if (usbPrinters.Count == 0)
        {
            throw new InvalidOperationException("No USB printers found");
        }

        // Use default USB printer or first available
        var printer = usbPrinters.FirstOrDefault(p => p.IsDefault) ?? usbPrinters[0];

        // Create temporary file
        var tempFile = Path.Combine(Path.GetTempPath(), $"print-{Guid.NewGuid()}.png");
        try
        {
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

            var startInfo = new ProcessStartInfo
            {
                FileName = "lp",
                Arguments = string.Join(" ", args.Select(a => $"\"{a}\"")),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                throw new InvalidOperationException("Failed to start print process");
            }

            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync(cancellationToken);
                throw new InvalidOperationException($"Print failed: {error}");
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var jobMatch = Regex.Match(output, @"request id is .+-(\d+)");
            var jobId = jobMatch.Success ? jobMatch.Groups[1].Value : output.Trim();

            _logger.LogInformation("Print job submitted to {PrinterName}, job ID: {JobId}", printer.Name, jobId);
            return new PrintResult(printer.Name, jobId);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try
                {
                    File.Delete(tempFile);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete temporary file: {TempFile}", tempFile);
                }
            }
        }
    }

    public async Task<List<PrinterInfo>> GetAvailablePrintersAsync(CancellationToken cancellationToken = default)
    {
        var printers = new List<PrinterInfo>();

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
                return printers;
            }

            var listOutput = await listProc.StandardOutput.ReadToEndAsync(cancellationToken);
            await listProc.WaitForExitAsync(cancellationToken);

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
                return printers;
            }

            var deviceOutput = await deviceProc.StandardOutput.ReadToEndAsync(cancellationToken);
            await deviceProc.WaitForExitAsync(cancellationToken);

            // Parse default printer
            var defaultMatch = Regex.Match(listOutput, @"system default destination: (.+)");
            var defaultPrinter = defaultMatch.Success ? defaultMatch.Groups[1].Value : string.Empty;

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

                if (!string.IsNullOrEmpty(deviceLine))
                {
                    var uriMatch = Regex.Match(deviceLine, @"device for (.+?): (.+)");
                    if (uriMatch.Success)
                    {
                        uri = uriMatch.Groups[2].Value;
                        isUSB = uri.Contains("usb", StringComparison.OrdinalIgnoreCase) ||
                                uri.Contains("bluetooth", StringComparison.OrdinalIgnoreCase);
                    }
                }

                printers.Add(new PrinterInfo(
                    printerName,
                    uri,
                    status,
                    printerName == defaultPrinter,
                    isUSB
                ));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get printers");
        }

        return printers;
    }

    public Task StartWatchingPrintersAsync(CancellationToken cancellationToken = default)
    {
        if (_watchTask != null)
        {
            return _watchTask;
        }

        _watchTask = Task.Run(async () =>
        {
            while (!_watchCts.Token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(1000, _watchCts.Token);

                    var printers = await GetAvailablePrintersAsync(_watchCts.Token);
                    var usbPrinters = printers.Where(p => p.IsUSB).ToList();

                    foreach (var printer in usbPrinters)
                    {
                        if (printer.Status.Contains("disabled", StringComparison.OrdinalIgnoreCase) ||
                            printer.Status.Contains("paused", StringComparison.OrdinalIgnoreCase))
                        {
                            await EnablePrinterAsync(printer.Name, _watchCts.Token);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error watching printers");
                }
            }
        }, _watchCts.Token);

        return _watchTask;
    }

    private async Task EnablePrinterAsync(string printerName, CancellationToken cancellationToken)
    {
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
            }

            var acceptProcess = new ProcessStartInfo
            {
                FileName = "cupsaccept",
                Arguments = $"\"{printerName}\"",
                UseShellExecute = false
            };

            using var acceptProc = Process.Start(acceptProcess);
            if (acceptProc != null)
            {
                await acceptProc.WaitForExitAsync(cancellationToken);
            }

            _logger.LogInformation("Enabled printer: {PrinterName}", printerName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enable printer: {PrinterName}", printerName);
        }
    }

    public void Dispose()
    {
        _watchCts.Cancel();
        _watchTask?.Wait(TimeSpan.FromSeconds(5));
        _watchCts.Dispose();
    }
}
