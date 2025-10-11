using Microsoft.Extensions.Logging;
using TkSharp.Core.Common;

namespace TkSharp.Core;

public class TkLog : Singleton<TkLog>, ILogger
{
    private readonly List<ILogger> _loggers = [];
    
    public LogLevel LogLevel { get; set; } =
#if DEBUG
        LogLevel.Trace;
#else
        LogLevel.Warning;
#endif

    public void Register(ILogger logger)
    {
        _loggers.Add(logger);
    }
    
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        foreach (var logger in _loggers) {
            logger.Log(logLevel, eventId, state, exception, FormatMessage);
        }
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel >= LogLevel;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    private static string FormatMessage<TState>(TState state, Exception? error)
    {
        return error is null
            ? state?.ToString() ?? string.Empty
            : $"{state}\n{error}";
    }
}