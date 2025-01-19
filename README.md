# Serilog.Sinks.GlobalEvent
Subscribe to Serilog log events globally
```csharp
var loggerConfiguration = new LoggerConfiguration()
    .WriteTo.GlobalEvent(config =>
    {
        config.MinimumLevel = LogEventLevel.Information;
    });

Log.Logger = loggerConfiguration.CreateLogger();

GlobalLoggerEvent.RegisterHandler((level, timestamp, renderedMessage) => Console.WriteLine(renderedMessage));

Log.Information("Test log message");
```