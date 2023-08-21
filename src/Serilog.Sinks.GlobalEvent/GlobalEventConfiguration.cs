using Serilog.Events;

namespace Serilog.Sinks.GlobalEvent
{
    public sealed class GlobalEventConfiguration
    {
        public LogEventLevel MinimumLevel { get; set; }
    }
}
