using Microsoft.Extensions.Logging;
using ALog = Android.Util.Log;

namespace DiskLens.Droid;

/// <summary>Routes Microsoft.Extensions.Logging to logcat under the "DiskLens" tag.</summary>
public sealed class LogcatLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new LogcatLogger(categoryName);
    public void Dispose() { }

    private sealed class LogcatLogger(string category) : ILogger
    {
        private readonly string _tag = "DiskLens";

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = $"[{category[(category.LastIndexOf('.') + 1)..]}] {formatter(state, exception)}";
            if (exception is not null) message += "\n" + exception;
            switch (logLevel)
            {
                case LogLevel.Trace or LogLevel.Debug: ALog.Debug(_tag, message); break;
                case LogLevel.Information: ALog.Info(_tag, message); break;
                case LogLevel.Warning: ALog.Warn(_tag, message); break;
                default: ALog.Error(_tag, message); break;
            }
        }
    }
}
