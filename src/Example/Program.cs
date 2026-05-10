using System;
using System.Threading.Tasks;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.GlobalEvent;

namespace Example
{
    public static class Program
    {
        public static async Task Main()
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.GlobalEvent(config => config.MinimumLevel = LogEventLevel.Information)
                .CreateLogger();

            using var consoleSubscription = GlobalLoggerEvent.RegisterHandler(
                (level, timestamp, renderedMessage) =>
                    Console.WriteLine($"[{timestamp:HH:mm:ss} {level}] {renderedMessage}"));

            using var exceptionSubscription = GlobalLoggerEvent.RegisterHandler((LogEvent e) =>
            {
                if (e.Exception != null)
                {
                    Console.WriteLine($"  -> exception: {e.Exception.GetType().Name}: {e.Exception.Message}");
                }
            });

            using var asyncSubscription = GlobalLoggerEvent.RegisterHandler(async (LogEvent e) =>
            {
                await Task.Delay(50);
                Console.WriteLine($"  -> async delivered: {e.Level} with {e.Properties.Count} property(ies)");
            });

            Log.Debug("filtered out by sink MinimumLevel");
            Log.Information("hello {Name}", "world");
            Log.Warning("disk usage at {Percent}%", 87);

            try
            {
                throw new InvalidOperationException("simulated failure");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "operation failed");
            }

            await GlobalLoggerEvent.FlushAsync(TimeSpan.FromSeconds(5));
            await Log.CloseAndFlushAsync();
        }
    }
}
