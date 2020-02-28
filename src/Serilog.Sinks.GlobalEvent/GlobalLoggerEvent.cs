using System;

namespace Serilog.Sinks.GlobalEvent
{
    public sealed class GlobalLoggerEvent
    {
        private static readonly Lazy<GlobalLoggerEvent> Instance = new Lazy<GlobalLoggerEvent>(() => new GlobalLoggerEvent());

        private GlobalLoggerEvent()
        {

        }

        private event Action<string> OnLog;

        public static void Emit(string logMessage)
        {
            Instance.Value.OnLog?.Invoke(logMessage);
        }

        public static void RegisterHandler(Action<string> handler)
        {
            Instance.Value.OnLog += handler;
        }

        public static void RemoveHandler(Action<string> handler)
        {
            Instance.Value.OnLog -= handler;
        }
    }
}
