using Serilog.Configuration;
using System;

namespace Serilog.Sinks.GlobalEvent
{
    public static class GlobalEventSinkExtensions
    {
        public static LoggerConfiguration GlobalEvent(
                  this LoggerSinkConfiguration loggerConfiguration,
                  IFormatProvider formatProvider = null)
        {
            return loggerConfiguration.Sink(new GlobalEventSink(formatProvider));
        }
    }
}
