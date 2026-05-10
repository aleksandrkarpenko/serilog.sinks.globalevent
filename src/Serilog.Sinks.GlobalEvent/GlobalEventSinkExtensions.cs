using System;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;

namespace Serilog.Sinks.GlobalEvent
{
    /// <summary>
    /// <see cref="LoggerSinkConfiguration"/> extensions for attaching
    /// <see cref="GlobalEventSink"/>.
    /// </summary>
    public static class GlobalEventSinkExtensions
    {
        /// <summary>
        /// Adds a <see cref="GlobalEventSink"/> to the Serilog pipeline so that
        /// every emitted event is forwarded to <see cref="GlobalLoggerEvent"/>
        /// subscribers.
        /// </summary>
        /// <param name="loggerConfiguration">The sink-configuration target, normally <c>WriteTo</c>.</param>
        /// <param name="configuration">Optional sink-configuration callback (for example, to set <see cref="GlobalEventConfiguration.MinimumLevel"/>).</param>
        /// <param name="formatProvider">Format provider used to render messages; pass <c>null</c> for the current culture.</param>
        /// <param name="restrictedToMinimumLevel">Minimum level for events to reach the sink. Applied by Serilog before the sink — combine with <paramref name="levelSwitch"/> for dynamic control.</param>
        /// <param name="levelSwitch">Optional level switch for runtime-adjustable filtering. Overrides <paramref name="restrictedToMinimumLevel"/> when provided.</param>
        /// <returns>The original <see cref="LoggerConfiguration"/>, to chain.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="loggerConfiguration"/> is <c>null</c>.</exception>
        public static LoggerConfiguration GlobalEvent(
            this LoggerSinkConfiguration loggerConfiguration,
            Action<GlobalEventConfiguration>? configuration = null,
            IFormatProvider? formatProvider = null,
            LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum,
            LoggingLevelSwitch? levelSwitch = null)
        {
            if (loggerConfiguration == null) throw new ArgumentNullException(nameof(loggerConfiguration));

            return loggerConfiguration.Sink(
                new GlobalEventSink(formatProvider, configuration),
                restrictedToMinimumLevel,
                levelSwitch);
        }
    }
}
