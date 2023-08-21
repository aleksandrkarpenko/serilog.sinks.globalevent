using System;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.GlobalEvent;

namespace Example
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            var loggerConfiguration = new LoggerConfiguration()
                .WriteTo.GlobalEvent(config =>
                {
                    config.MinimumLevel = LogEventLevel.Information;
                });

            Log.Logger = loggerConfiguration.CreateLogger();

            GlobalLoggerEvent.RegisterHandler((level, timestamp, renderedMessage) => Console.WriteLine(renderedMessage));

            Log.Information("Test log message");

            Console.ReadLine();
        }
    }
}
