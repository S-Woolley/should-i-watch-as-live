using System.Globalization;
using DbUp.Engine.Output;

namespace ShouldIWatch.Api.Data;

/// <summary>Routes DbUp's string.Format-style log messages to <see cref="ILogger"/>.</summary>
internal sealed class DbUpLogger(ILogger logger) : IUpgradeLog
{
    public void LogTrace(string format, params object[] args) => Write(LogLevel.Trace, null, format, args);
    public void LogDebug(string format, params object[] args) => Write(LogLevel.Debug, null, format, args);
    public void LogInformation(string format, params object[] args) => Write(LogLevel.Information, null, format, args);
    public void LogWarning(string format, params object[] args) => Write(LogLevel.Warning, null, format, args);
    public void LogError(string format, params object[] args) => Write(LogLevel.Error, null, format, args);
    public void LogError(Exception ex, string format, params object[] args) => Write(LogLevel.Error, ex, format, args);

    private void Write(LogLevel level, Exception? exception, string format, object[] args)
    {
        if (!logger.IsEnabled(level))
        {
            return;
        }

        var message = args.Length == 0 ? format : string.Format(CultureInfo.InvariantCulture, format, args);
#pragma warning disable CA1848, CA2254 // Messages come pre-formatted from DbUp.
        logger.Log(level, exception, "{DbUpMessage}", message);
#pragma warning restore CA1848, CA2254
    }
}
