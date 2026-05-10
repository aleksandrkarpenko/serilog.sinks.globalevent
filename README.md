# Serilog.Sinks.GlobalEvent

Subscribe to Serilog log events globally, from anywhere in the process — without
having to thread an `ILogger` or a custom sink through your code.

## Usage

Register the sink and subscribe a handler:

```csharp
var loggerConfiguration = new LoggerConfiguration()
    .WriteTo.GlobalEvent(config =>
    {
        config.MinimumLevel = LogEventLevel.Information;
    });

Log.Logger = loggerConfiguration.CreateLogger();

GlobalLoggerEvent.RegisterHandler((level, timestamp, renderedMessage) =>
    Console.WriteLine(renderedMessage));

Log.Information("Test log message");
```

`MinimumLevel` filters events emitted to handlers — events below it are dropped.
The default is `Verbose`, i.e. all events pass through.

The extension also accepts the standard Serilog sink parameters
`restrictedToMinimumLevel` and `levelSwitch` for filtering at the pipeline
level. Use a `LoggingLevelSwitch` for runtime-adjustable filtering:

```csharp
var levelSwitch = new LoggingLevelSwitch(LogEventLevel.Warning);
new LoggerConfiguration()
    .WriteTo.GlobalEvent(levelSwitch: levelSwitch)
    .CreateLogger();

// later, change at runtime:
levelSwitch.MinimumLevel = LogEventLevel.Information;
```

### Full `LogEvent` access

If you need exception, properties, or message template, register an
`Action<LogEvent>` instead of the rendered-message overload:

```csharp
GlobalLoggerEvent.RegisterHandler((LogEvent e) =>
{
    if (e.Exception != null)
    {
        Telemetry.Track(e.Exception, e.Properties);
    }
});
```

### Async handlers

Register `Func<LogEvent, Task>` for async work (HTTP, queue, DB writes). Async
handlers run fire-and-forget — the calling thread returns immediately, and
faulted tasks are routed to `Serilog.Debugging.SelfLog`:

```csharp
GlobalLoggerEvent.RegisterHandler(async (LogEvent e) =>
{
    await sink.SendAsync(e);
});
```

For cancellation support — typically to stop long-running handlers on
application shutdown — register a `Func<LogEvent, CancellationToken, Task>`.
The token is signalled by `ShutdownAsync`:

```csharp
GlobalLoggerEvent.RegisterHandler(async (LogEvent e, CancellationToken ct) =>
{
    await sink.SendAsync(e, ct);
});
```

There is no back-pressure: every event spawns an in-flight task per async
handler. Under bursty logging this can pile up — if you need batching, layer
[`Serilog.Sinks.Async`](https://github.com/serilog/serilog-sinks-async) on top.

### Graceful shutdown

To wait for in-flight async handlers to finish (e.g. before exiting the
process):

```csharp
await GlobalLoggerEvent.FlushAsync(TimeSpan.FromSeconds(5));
```

To cancel cancellable handlers and then drain:

```csharp
await GlobalLoggerEvent.ShutdownAsync(TimeSpan.FromSeconds(5));
```

Both methods return after at most `timeout`; tasks still running after that
deadline are abandoned (the process exit will tear them down). After
`ShutdownAsync`, `GlobalLoggerEvent.ShutdownToken` stays in the cancelled state
for the rest of the process lifetime.

### Unsubscribing

`RegisterHandler` returns an `IDisposable`. Dispose it to unsubscribe — no need
to keep a reference to the original delegate:

```csharp
var subscription = GlobalLoggerEvent.RegisterHandler(e => Telemetry.Track(e));
// ...
subscription.Dispose();
```

Or scope it with `using` so the handler unregisters automatically:

```csharp
using var _ = GlobalLoggerEvent.RegisterHandler((level, _, msg) =>
    Console.WriteLine($"[{level}] {msg}"));

// handler stays active until the enclosing scope exits
```

Disposing twice is a no-op. To drop every subscriber at once (useful in tests,
hot-reload, or shutdown):

```csharp
GlobalLoggerEvent.ClearHandlers();
```

### Notes

- Handlers are stored in a process-global event. They are invoked on the thread
  that produced the log event — keep them fast and non-blocking.
- Exceptions thrown by a handler are isolated: they are routed to
  `Serilog.Debugging.SelfLog` and do not prevent other handlers from running
  for the same event.
