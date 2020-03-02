using Serilog.Core;
using Serilog.Events;
using System;

namespace Serilog.Sinks.GlobalEvent
{
    public sealed class GlobalEventSink : ILogEventSink
    {
        private readonly IFormatProvider _formatProvider;
        private readonly GlobalEventConfiguration configuration;

        public GlobalEventSink(IFormatProvider formatProvider, Action<GlobalEventConfiguration> action = null)
        {
            configuration = new GlobalEventConfiguration();

            action?.Invoke(configuration);

            _formatProvider = formatProvider;
        }

        public void Emit(LogEvent logEvent)
        {
            if (logEvent.Level > configuration.MinimumLevel)
                return;

            GlobalLoggerEvent.Emit(logEvent.RenderMessage(_formatProvider));
        }
    }
}
