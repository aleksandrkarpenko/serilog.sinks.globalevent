using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace Serilog.Sinks.GlobalEvent.Tests
{
    [Collection("GlobalEventSequential")]
    public sealed class GlobalEventSinkTests : IDisposable
    {
        public GlobalEventSinkTests()
        {
            GlobalLoggerEvent.ResetForTests();
        }

        public void Dispose()
        {
            GlobalLoggerEvent.ResetForTests();
            Serilog.Debugging.SelfLog.Disable();
        }

        [Fact]
        public void Emits_events_at_or_above_minimum_level()
        {
            var received = new List<LogEventLevel>();
            GlobalLoggerEvent.RegisterHandler((level, _, _) => received.Add(level));

            using var logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.GlobalEvent(c => c.MinimumLevel = LogEventLevel.Information)
                .CreateLogger();

            logger.Verbose("v");
            logger.Debug("d");
            logger.Information("i");
            logger.Warning("w");
            logger.Error("e");
            logger.Fatal("f");

            Assert.Equal(
                new[]
                {
                    LogEventLevel.Information,
                    LogEventLevel.Warning,
                    LogEventLevel.Error,
                    LogEventLevel.Fatal,
                },
                received);
        }

        [Fact]
        public void Default_minimum_level_lets_every_event_through()
        {
            var received = new List<LogEventLevel>();
            GlobalLoggerEvent.RegisterHandler((level, _, _) => received.Add(level));

            using var logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.GlobalEvent()
                .CreateLogger();

            logger.Verbose("v");
            logger.Information("i");
            logger.Fatal("f");

            Assert.Equal(3, received.Count);
        }

        [Fact]
        public void Disposing_subscription_unsubscribes_the_handler()
        {
            var count = 0;
            var subscription = GlobalLoggerEvent.RegisterHandler((_, _, _) => count++);

            using (var logger = new LoggerConfiguration()
                       .MinimumLevel.Verbose()
                       .WriteTo.GlobalEvent()
                       .CreateLogger())
            {
                logger.Information("first");
            }

            subscription.Dispose();

            using (var logger = new LoggerConfiguration()
                       .MinimumLevel.Verbose()
                       .WriteTo.GlobalEvent()
                       .CreateLogger())
            {
                logger.Information("second");
            }

            Assert.Equal(1, count);
        }

        [Fact]
        public void Disposing_subscription_twice_is_a_no_op()
        {
            var count = 0;
            var subscription = GlobalLoggerEvent.RegisterHandler((_, _, _) => count++);

            subscription.Dispose();
            subscription.Dispose();

            using var logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.GlobalEvent()
                .CreateLogger();

            logger.Information("nothing");

            Assert.Equal(0, count);
        }

        [Fact]
        public void LogEvent_subscription_can_be_disposed_independently()
        {
            var simpleCount = 0;
            var eventCount = 0;

            GlobalLoggerEvent.RegisterHandler((_, _, _) => simpleCount++);
            var eventSub = GlobalLoggerEvent.RegisterHandler((LogEvent _) => eventCount++);

            eventSub.Dispose();

            using var logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.GlobalEvent()
                .CreateLogger();

            logger.Information("ping");

            Assert.Equal(1, simpleCount);
            Assert.Equal(0, eventCount);
        }

        [Fact]
        public void ClearHandlers_drops_every_subscriber()
        {
            GlobalLoggerEvent.RegisterHandler((_, _, _) => throw new Xunit.Sdk.XunitException("simple handler should not fire"));
            GlobalLoggerEvent.RegisterHandler((LogEvent _) => throw new Xunit.Sdk.XunitException("event handler should not fire"));

            GlobalLoggerEvent.ClearHandlers();

            using var logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.GlobalEvent()
                .CreateLogger();

            logger.Information("nothing should fire");
        }

        [Fact]
        public void LogEvent_handler_receives_exception_and_properties()
        {
            LogEvent? captured = null;
            GlobalLoggerEvent.RegisterHandler((LogEvent e) => captured = e);

            using var logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.GlobalEvent()
                .CreateLogger();

            var ex = new InvalidOperationException("boom");
            logger.Error(ex, "oops {Code}", 42);

            Assert.NotNull(captured);
            Assert.Same(ex, captured!.Exception);
            Assert.Equal(LogEventLevel.Error, captured.Level);
            Assert.True(captured.Properties.ContainsKey("Code"));
        }

        [Fact]
        public async Task Async_handler_receives_event()
        {
            var tcs = new TaskCompletionSource<LogEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var sub = GlobalLoggerEvent.RegisterHandler(async (LogEvent e) =>
            {
                await Task.Yield();
                tcs.TrySetResult(e);
            });

            await using var logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.GlobalEvent()
                .CreateLogger();

            logger.Information("async ping {N}", 7);

            var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(LogEventLevel.Information, received.Level);
            Assert.True(received.Properties.ContainsKey("N"));
        }

        [Fact]
        public async Task Disposing_async_subscription_unsubscribes()
        {
            var count = 0;
            var firstReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var sub = GlobalLoggerEvent.RegisterHandler((LogEvent _) =>
            {
                Interlocked.Increment(ref count);
                firstReceived.TrySetResult();
                return Task.CompletedTask;
            });

            using (var logger = new LoggerConfiguration()
                       .MinimumLevel.Verbose()
                       .WriteTo.GlobalEvent()
                       .CreateLogger())
            {
                logger.Information("first");
                await firstReceived.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            }

            sub.Dispose();

            using (var logger = new LoggerConfiguration()
                       .MinimumLevel.Verbose()
                       .WriteTo.GlobalEvent()
                       .CreateLogger())
            {
                logger.Information("second");
            }

            Assert.Equal(1, count);
        }

        [Fact]
        public async Task Async_handler_faults_are_routed_to_selflog()
        {
            var faultCaptured = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            Serilog.Debugging.SelfLog.Enable(msg =>
            {
                if (msg.Contains("async handler faulted"))
                {
                    faultCaptured.TrySetResult(msg);
                }
            });

            using var sub = GlobalLoggerEvent.RegisterHandler(async (LogEvent _) =>
            {
                await Task.Yield();
                throw new InvalidOperationException("boom");
            });

            await using var logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.GlobalEvent()
                .CreateLogger();

            logger.Information("trigger");

            var msg = await faultCaptured.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Contains("InvalidOperationException", msg);
        }

        [Fact]
        public async Task Sync_faulted_task_from_handler_is_routed_to_selflog()
        {
            var faultCaptured = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            Serilog.Debugging.SelfLog.Enable(msg =>
            {
                if (msg.Contains("async handler"))
                {
                    faultCaptured.TrySetResult(msg);
                }
            });

            using var sub = GlobalLoggerEvent.RegisterHandler((LogEvent _) =>
                Task.FromException(new InvalidOperationException("sync-faulted")));

            using var logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.GlobalEvent()
                .CreateLogger();

            logger.Information("trigger");

            var msg = await faultCaptured.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Contains("InvalidOperationException", msg);
        }

        [Fact]
        public async Task Synchronous_handler_throw_is_routed_to_selflog()
        {
            var faultCaptured = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            Serilog.Debugging.SelfLog.Enable(msg =>
            {
                if (msg.Contains("threw synchronously"))
                {
                    faultCaptured.TrySetResult(msg);
                }
            });

            Func<LogEvent, Task> handler = _ => throw new InvalidOperationException("sync throw");
            using var sub = GlobalLoggerEvent.RegisterHandler(handler);

            using var logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.GlobalEvent()
                .CreateLogger();

            logger.Information("trigger");

            var msg = await faultCaptured.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Contains("InvalidOperationException", msg);
        }

        [Fact]
        public async Task FlushAsync_waits_for_inflight_async_handlers()
        {
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            using var sub = GlobalLoggerEvent.RegisterHandler(async (LogEvent _) =>
            {
                entered.TrySetResult();
                await release.Task;
            });

            await using var logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.GlobalEvent()
                .CreateLogger();

            logger.Information("emit");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            var flush = GlobalLoggerEvent.FlushAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(flush.IsCompleted);

            release.TrySetResult();
            await flush.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task FlushAsync_returns_when_no_inflight_work()
        {
            var flush = GlobalLoggerEvent.FlushAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
            Assert.True(flush.IsCompletedSuccessfully);
            await flush;
        }

        [Fact]
        public async Task FlushAsync_returns_after_timeout_when_handlers_hang()
        {
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            using var sub = GlobalLoggerEvent.RegisterHandler(async (LogEvent _) =>
            {
                entered.TrySetResult();
                await release.Task;
            });

            using var logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.GlobalEvent()
                .CreateLogger();

            logger.Information("emit");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            var sw = Stopwatch.StartNew();
            await GlobalLoggerEvent.FlushAsync(TimeSpan.FromMilliseconds(150), TestContext.Current.CancellationToken);
            sw.Stop();

            Assert.InRange(sw.ElapsedMilliseconds, 100, 5_000);

            release.TrySetResult();
        }

        [Fact]
        public async Task Cancellable_handler_receives_a_live_token()
        {
            CancellationToken received = default;
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            using var sub = GlobalLoggerEvent.RegisterHandler((LogEvent _, CancellationToken ct) =>
            {
                received = ct;
                entered.TrySetResult();
                return Task.CompletedTask;
            });

            await using var logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.GlobalEvent()
                .CreateLogger();

            logger.Information("emit");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            Assert.True(received.CanBeCanceled);
            Assert.False(received.IsCancellationRequested);
        }

        [Fact]
        public async Task ShutdownAsync_cancels_inflight_handler_and_flushes()
        {
            var observedCancellation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            using var sub = GlobalLoggerEvent.RegisterHandler(async (LogEvent _, CancellationToken ct) =>
            {
                entered.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, ct);
                }
                catch (OperationCanceledException)
                {
                    observedCancellation.TrySetResult();
                    throw;
                }
            });

            await using var logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.GlobalEvent()
                .CreateLogger();

            logger.Information("emit");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            await GlobalLoggerEvent.ShutdownAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            await observedCancellation.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.True(GlobalLoggerEvent.ShutdownToken.IsCancellationRequested);
        }

        [Fact]
        public async Task Sync_handler_throw_is_isolated_and_routed_to_selflog()
        {
            var faultCaptured = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            Serilog.Debugging.SelfLog.Enable(msg =>
            {
                if (msg.Contains("sync handler threw"))
                {
                    faultCaptured.TrySetResult(msg);
                }
            });

            var afterThrowCount = 0;
            var asyncCount = 0;

            GlobalLoggerEvent.RegisterHandler((LogEvent _) => throw new InvalidOperationException("boom"));
            GlobalLoggerEvent.RegisterHandler((LogEvent _) => Interlocked.Increment(ref afterThrowCount));
            GlobalLoggerEvent.RegisterHandler((LogEvent _) =>
            {
                Interlocked.Increment(ref asyncCount);
                return Task.CompletedTask;
            });

            using var logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.GlobalEvent()
                .CreateLogger();

            logger.Information("trigger");

            var msg = await faultCaptured.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Contains("InvalidOperationException", msg);
            Assert.Equal(1, afterThrowCount);
            Assert.Equal(1, asyncCount);
        }

        [Fact]
        public async Task Simple_handler_throw_is_isolated_and_routed_to_selflog()
        {
            var faultCaptured = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            Serilog.Debugging.SelfLog.Enable(msg =>
            {
                if (msg.Contains("sync handler threw"))
                {
                    faultCaptured.TrySetResult(msg);
                }
            });

            var afterThrowCount = 0;

            GlobalLoggerEvent.RegisterHandler((Action<LogEventLevel, DateTimeOffset, string>)((_, _, _) =>
                throw new InvalidOperationException("boom")));
            GlobalLoggerEvent.RegisterHandler((_, _, _) => Interlocked.Increment(ref afterThrowCount));

            using var logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.GlobalEvent()
                .CreateLogger();

            logger.Information("trigger");

            var msg = await faultCaptured.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Contains("InvalidOperationException", msg);
            Assert.Equal(1, afterThrowCount);
        }

        [Fact]
        public async Task Cancellable_subscription_dispose_unsubscribes()
        {
            var count = 0;
            var firstReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var sub = GlobalLoggerEvent.RegisterHandler((LogEvent _, CancellationToken _) =>
            {
                Interlocked.Increment(ref count);
                firstReceived.TrySetResult();
                return Task.CompletedTask;
            });

            using (var logger = new LoggerConfiguration()
                       .MinimumLevel.Verbose()
                       .WriteTo.GlobalEvent()
                       .CreateLogger())
            {
                logger.Information("first");
                await firstReceived.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            }

            sub.Dispose();

            using (var logger = new LoggerConfiguration()
                       .MinimumLevel.Verbose()
                       .WriteTo.GlobalEvent()
                       .CreateLogger())
            {
                logger.Information("second");
            }

            Assert.Equal(1, count);
        }

        [Fact]
        public void Same_delegate_registered_twice_unsubscribes_one_at_a_time()
        {
            var count = 0;
            void Handler(LogEvent _) => Interlocked.Increment(ref count);

            var first = GlobalLoggerEvent.RegisterHandler(Handler);
            var second = GlobalLoggerEvent.RegisterHandler(Handler);

            using (var logger = new LoggerConfiguration()
                       .MinimumLevel.Verbose()
                       .WriteTo.GlobalEvent()
                       .CreateLogger())
            {
                logger.Information("both fire");
            }

            Assert.Equal(2, count);

            first.Dispose();
            count = 0;

            using (var logger = new LoggerConfiguration()
                       .MinimumLevel.Verbose()
                       .WriteTo.GlobalEvent()
                       .CreateLogger())
            {
                logger.Information("one remains");
            }

            Assert.Equal(1, count);

            second.Dispose();
            count = 0;

            using (var logger = new LoggerConfiguration()
                       .MinimumLevel.Verbose()
                       .WriteTo.GlobalEvent()
                       .CreateLogger())
            {
                logger.Information("none");
            }

            Assert.Equal(0, count);
        }

        [Fact]
        public void RestrictedToMinimumLevel_filters_events_before_sink()
        {
            var received = new List<LogEventLevel>();
            GlobalLoggerEvent.RegisterHandler((level, _, _) => received.Add(level));

            using var logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.GlobalEvent(restrictedToMinimumLevel: LogEventLevel.Warning)
                .CreateLogger();

            logger.Information("dropped");
            logger.Warning("kept");
            logger.Error("kept");

            Assert.Equal(
                new[] { LogEventLevel.Warning, LogEventLevel.Error },
                received);
        }

        [Fact]
        public void LoggingLevelSwitch_filters_events_dynamically()
        {
            var received = new List<LogEventLevel>();
            GlobalLoggerEvent.RegisterHandler((level, _, _) => received.Add(level));

            var levelSwitch = new LoggingLevelSwitch(LogEventLevel.Warning);

            using var logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.GlobalEvent(levelSwitch: levelSwitch)
                .CreateLogger();

            logger.Information("dropped-1");
            logger.Warning("kept-1");

            levelSwitch.MinimumLevel = LogEventLevel.Information;

            logger.Information("kept-2");

            Assert.Equal(
                new[] { LogEventLevel.Warning, LogEventLevel.Information },
                received);
        }

        [Fact]
        public async Task FlushAsync_with_infinite_timeout_completes_when_handlers_finish()
        {
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            using var sub = GlobalLoggerEvent.RegisterHandler(async (LogEvent _) =>
            {
                entered.TrySetResult();
                await release.Task;
            });

            using var logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.GlobalEvent()
                .CreateLogger();

            logger.Information("emit");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            var flush = GlobalLoggerEvent.FlushAsync(Timeout.InfiniteTimeSpan, TestContext.Current.CancellationToken);
            Assert.False(flush.IsCompleted);

            release.TrySetResult();
            await flush.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task RegisterHandler_throws_on_null_handler()
        {
            await Task.Yield();

            Assert.Throws<ArgumentNullException>(() =>
                GlobalLoggerEvent.RegisterHandler((Action<LogEventLevel, DateTimeOffset, string>)null!));
            Assert.Throws<ArgumentNullException>(() =>
                GlobalLoggerEvent.RegisterHandler((Action<LogEvent>)null!));
            Assert.Throws<ArgumentNullException>(() =>
                GlobalLoggerEvent.RegisterHandler((Func<LogEvent, Task>)null!));
            Assert.Throws<ArgumentNullException>(() =>
                GlobalLoggerEvent.RegisterHandler((Func<LogEvent, CancellationToken, Task>)null!));
        }

        [Fact]
        public void Both_handler_overloads_receive_the_same_event()
        {
            string? simpleMessage = null;
            LogEvent? eventCopy = null;

            GlobalLoggerEvent.RegisterHandler((_, _, msg) => simpleMessage = msg);
            GlobalLoggerEvent.RegisterHandler((LogEvent e) => eventCopy = e);

            using var logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.GlobalEvent()
                .CreateLogger();

            logger.Information("hello {Name}", "world");

            Assert.Equal("hello \"world\"", simpleMessage);
            Assert.NotNull(eventCopy);
            Assert.Equal("hello {Name}", eventCopy!.MessageTemplate.Text);
        }
    }
}
