using System;
using Serilog.Core;
using Serilog.Events;

namespace Serilog.Sinks.GlobalEvent
{
    /// <summary>
    /// Serilog sink that forwards events to <see cref="GlobalLoggerEvent"/>.
    /// Attach via <see cref="GlobalEventSinkExtensions.GlobalEvent"/>.
    /// </summary>
    public sealed class GlobalEventSink : ILogEventSink
    {
        private readonly IFormatProvider? _formatProvider;
        private readonly GlobalEventConfiguration _configuration;

        /// <summary>
        /// Creates a new sink instance.
        /// </summary>
        /// <param name="formatProvider">Format provider used to render the message for rendered-message subscribers; pass <c>null</c> to use the current culture.</param>
        /// <param name="configure">Optional callback to configure the sink (for example, to set <see cref="GlobalEventConfiguration.MinimumLevel"/>).</param>
        public GlobalEventSink(IFormatProvider? formatProvider, Action<GlobalEventConfiguration>? configure = null)
        {
            _formatProvider = formatProvider;
            _configuration = new GlobalEventConfiguration();

            configure?.Invoke(_configuration);
        }

        /// <inheritdoc />
        public void Emit(LogEvent logEvent)
        {
            if (logEvent == null) throw new ArgumentNullException(nameof(logEvent));

            if (logEvent.Level < _configuration.MinimumLevel)
            {
                return;
            }

            GlobalLoggerEvent.Emit(logEvent, _formatProvider);
        }
    }
}
