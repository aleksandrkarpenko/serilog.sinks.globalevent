using Serilog.Events;

namespace Serilog.Sinks.GlobalEvent
{
    /// <summary>
    /// Configuration for <see cref="GlobalEventSink"/>.
    /// </summary>
    public sealed class GlobalEventConfiguration
    {
        /// <summary>
        /// Minimum level of events that the sink forwards to subscribers.
        /// Events below this level are dropped before reaching
        /// <see cref="GlobalLoggerEvent"/>. Defaults to <see cref="LogEventLevel.Verbose"/>
        /// (everything passes).
        /// </summary>
        public LogEventLevel MinimumLevel { get; set; } = LogEventLevel.Verbose;
    }
}
