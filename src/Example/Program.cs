using System;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.GlobalEvent;

namespace Example
{
    class Program
    {
        static void Main(string[] args)
        {
            var loggerConfiguraion = new LoggerConfiguration()
                .WriteTo.GlobalEvent(config =>
                {
                    config.MinimumLevel = LogEventLevel.Information;
                });

            Log.Logger = loggerConfiguraion.CreateLogger();

            GlobalLoggerEvent.RegisterHandler(eventMessage => Console.WriteLine(eventMessage));

            Log.Information("Test log message");

            Console.ReadLine();
        }
    }
}
