namespace StickerDream.Server.Services;

/// <summary>
/// Service for printing to USB/Bluetooth thermal printers
/// </summary>
public interface IPrinterService
{
    Task<PrintResult> PrintImageAsync(byte[] imageData, PrintOptions? options = null, CancellationToken cancellationToken = default);
    Task<List<PrinterInfo>> GetAvailablePrintersAsync(CancellationToken cancellationToken = default);
    Task StartWatchingPrintersAsync(CancellationToken cancellationToken = default);
}

public record PrintResult(string PrinterName, string JobId);
public record PrintOptions(bool FitToPage = true, int Copies = 1, string? Media = null);
public record PrinterInfo(string Name, string Uri, string Status, bool IsDefault, bool IsUSB);
