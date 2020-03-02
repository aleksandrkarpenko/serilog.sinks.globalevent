using Serilog.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Serilog.Sinks.GlobalEvent
{
    public sealed class GlobalEventConfiguration
    {
        public LogEventLevel MinimumLevel { get; set; }
    }
}
