namespace StickerDream.Server.Middleware;

/// <summary>
/// Middleware for logging HTTP requests and responses
/// </summary>
public class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    private readonly RequestDelegate _next = next;
    private readonly ILogger<RequestLoggingMiddleware> _logger = logger;

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var requestId = Guid.NewGuid().ToString("N")[..8];
        
        // Add request ID to context for correlation
        context.Items["RequestId"] = requestId;

        var method = context.Request.Method;
        var path = context.Request.Path;
        var queryString = context.Request.QueryString.ToString();
        var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = context.Request.Headers.UserAgent.ToString();

        _logger.LogInformation(
            "HTTP request started. RequestId: {RequestId}, Method: {Method}, Path: {Path}, QueryString: {QueryString}, ClientIp: {ClientIp}, UserAgent: {UserAgent}",
            requestId, method, path, queryString, clientIp, userAgent);

        try
        {
            await _next(context);
            
            stopwatch.Stop();
            var statusCode = context.Response.StatusCode;

            _logger.LogInformation(
                "HTTP request completed. RequestId: {RequestId}, Method: {Method}, Path: {Path}, StatusCode: {StatusCode}, DurationMs: {DurationMs}",
                requestId, method, path, statusCode, stopwatch.ElapsedMilliseconds);

            // Log warnings for slow requests
            if (stopwatch.ElapsedMilliseconds > 5000)
            {
                _logger.LogWarning(
                    "Slow HTTP request detected. RequestId: {RequestId}, Method: {Method}, Path: {Path}, DurationMs: {DurationMs}",
                    requestId, method, path, stopwatch.ElapsedMilliseconds);
            }

            // Log errors for 5xx status codes
            if (statusCode >= 500)
            {
                _logger.LogError(
                    "HTTP request returned server error. RequestId: {RequestId}, Method: {Method}, Path: {Path}, StatusCode: {StatusCode}",
                    requestId, method, path, statusCode);
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "HTTP request failed with exception. RequestId: {RequestId}, Method: {Method}, Path: {Path}, DurationMs: {DurationMs}",
                requestId, method, path, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
