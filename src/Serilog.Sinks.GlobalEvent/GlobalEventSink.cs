using Serilog.Core;
using Serilog.Events;
using System;

namespace Serilog.Sinks.GlobalEvent
{
    public sealed class GlobalEventSink : ILogEventSink
    {
        private readonly IFormatProvider _formatProvider;

        public GlobalEventSink(IFormatProvider formatProvider)
        {
            _formatProvider = formatProvider;
        }

        public void Emit(LogEvent logEvent)
        {
            GlobalLoggerEvent.Emit(logEvent.RenderMessage(_formatProvider));
        }
    }
}
