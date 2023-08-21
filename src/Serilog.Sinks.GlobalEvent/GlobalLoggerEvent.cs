using System;
using Serilog.Events;

namespace Serilog.Sinks.GlobalEvent
{
    public sealed class GlobalLoggerEvent
    {
        private static readonly Lazy<GlobalLoggerEvent> Instance = new Lazy<GlobalLoggerEvent>(() => new GlobalLoggerEvent());

        private GlobalLoggerEvent()
        {

        }

        private event Action<LogEventLevel, DateTimeOffset, string> OnLog;

        public static void Emit(LogEventLevel level, DateTimeOffset timestamp, string logMessage)
        {
            Instance.Value.OnLog?.Invoke(level, timestamp, logMessage);
        }

        public static void RegisterHandler(Action<LogEventLevel, DateTimeOffset, string> handler)
        {
            Instance.Value.OnLog += handler;
        }

        public static void RemoveHandler(Action<LogEventLevel, DateTimeOffset, string> handler)
        {
            Instance.Value.OnLog -= handler;
        }
    }
}
