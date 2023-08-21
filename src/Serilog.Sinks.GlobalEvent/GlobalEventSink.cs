using Serilog.Core;
using Serilog.Events;
using System;

namespace Serilog.Sinks.GlobalEvent
{
    public sealed class GlobalEventSink : ILogEventSink
    {
        private readonly IFormatProvider _formatProvider;
        private readonly GlobalEventConfiguration _configuration;

        public GlobalEventSink(IFormatProvider formatProvider, Action<GlobalEventConfiguration> action = null)
        {
            _configuration = new GlobalEventConfiguration();

            action?.Invoke(_configuration);

            _formatProvider = formatProvider;
        }

        public void Emit(LogEvent logEvent)
        {
            if (logEvent.Level > _configuration.MinimumLevel)
            {
                return;
            }

            GlobalLoggerEvent.Emit(logEvent.Level, logEvent.Timestamp, logEvent.RenderMessage(_formatProvider));
        }
    }
}
