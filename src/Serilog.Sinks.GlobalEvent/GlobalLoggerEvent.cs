using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog.Debugging;
using Serilog.Events;

namespace Serilog.Sinks.GlobalEvent
{
    /// <summary>
    /// Process-global event hub for Serilog log events. Any
    /// <see cref="GlobalEventSink"/> attached to a Serilog pipeline forwards its
    /// events here, where any number of subscribers — synchronous, asynchronous,
    /// or cancellable — can react.
    /// </summary>
    public static class GlobalLoggerEvent
    {
        private static event Action<LogEventLevel, DateTimeOffset, string>? OnLog;
        private static event Action<LogEvent>? OnLogEvent;
        private static event Func<LogEvent, Task>? OnLogEventAsync;
        private static event Func<LogEvent, CancellationToken, Task>? OnLogEventAsyncCancellable;

        private static readonly ConcurrentDictionary<Task, byte> InFlight = new ConcurrentDictionary<Task, byte>();
        private static readonly Action<Task> RemoveFromInFlight = task => InFlight.TryRemove(task, out _);

        private static CancellationTokenSource _shutdownCts = new CancellationTokenSource();

        /// <summary>
        /// Token signalled by <see cref="ShutdownAsync(TimeSpan, CancellationToken)"/>.
        /// Long-running handlers can observe it to cooperate with application
        /// shutdown without using the cancellable handler overload.
        /// </summary>
        public static CancellationToken ShutdownToken => Volatile.Read(ref _shutdownCts).Token;

        /// <summary>
        /// Invokes only the rendered-message subscribers. <b>Does not</b> invoke
        /// <see cref="LogEvent"/>, async, or cancellable subscribers — sinks
        /// should drive emission via the internal <c>LogEvent</c> overload, and
        /// callers should use Serilog's pipeline rather than this method.
        /// </summary>
        /// <param name="level">Log event level.</param>
        /// <param name="timestamp">Log event timestamp.</param>
        /// <param name="logMessage">Rendered log message.</param>
        [Obsolete("This overload bypasses LogEvent, async, and cancellable subscribers. Let GlobalEventSink drive emission via Serilog instead.")]
        public static void Emit(LogEventLevel level, DateTimeOffset timestamp, string logMessage)
        {
            var simple = OnLog;
            if (simple != null)
            {
                DispatchSimple(simple, level, timestamp, logMessage);
            }
        }

        internal static void Emit(LogEvent logEvent, IFormatProvider? formatProvider)
        {
            var sync = OnLogEvent;
            if (sync != null)
            {
                DispatchSync(sync, logEvent);
            }

            DispatchAsync(OnLogEventAsync, logEvent);
            DispatchCancellable(OnLogEventAsyncCancellable, logEvent);

            var simple = OnLog;
            if (simple != null)
            {
                DispatchSimple(simple, logEvent.Level, logEvent.Timestamp, logEvent.RenderMessage(formatProvider));
            }
        }

        /// <summary>
        /// Subscribes a synchronous handler that receives the event level,
        /// timestamp, and rendered message. The handler runs on the thread that
        /// produced the log event. Exceptions thrown by the handler are routed
        /// to <see cref="SelfLog"/> and do not affect other subscribers.
        /// </summary>
        /// <param name="handler">Handler invoked for each emitted event.</param>
        /// <returns>Subscription whose <see cref="IDisposable.Dispose"/> unregisters the handler. Disposing twice is a no-op.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="handler"/> is <c>null</c>.</exception>
        public static IDisposable RegisterHandler(Action<LogEventLevel, DateTimeOffset, string> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            OnLog += handler;
            return new Subscription(() => OnLog -= handler);
        }

        /// <summary>
        /// Subscribes a synchronous handler that receives the full
        /// <see cref="LogEvent"/>, including exception, properties, and message
        /// template. The handler runs on the thread that produced the log event.
        /// Exceptions thrown by the handler are routed to <see cref="SelfLog"/>
        /// and do not affect other subscribers.
        /// </summary>
        /// <param name="handler">Handler invoked for each emitted event.</param>
        /// <returns>Subscription whose <see cref="IDisposable.Dispose"/> unregisters the handler.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="handler"/> is <c>null</c>.</exception>
        public static IDisposable RegisterHandler(Action<LogEvent> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            OnLogEvent += handler;
            return new Subscription(() => OnLogEvent -= handler);
        }

        /// <summary>
        /// Subscribes an asynchronous handler. The returned task is awaited
        /// fire-and-forget — the calling thread (the one that produced the log
        /// event) returns at the first <c>await</c>. Faulted tasks are routed
        /// to <see cref="SelfLog"/>. In-flight tasks are tracked and can be
        /// drained via <see cref="FlushAsync(TimeSpan, CancellationToken)"/>.
        /// </summary>
        /// <param name="handler">Async handler invoked for each emitted event.</param>
        /// <returns>Subscription whose <see cref="IDisposable.Dispose"/> unregisters the handler.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="handler"/> is <c>null</c>.</exception>
        public static IDisposable RegisterHandler(Func<LogEvent, Task> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            OnLogEventAsync += handler;
            return new Subscription(() => OnLogEventAsync -= handler);
        }

        /// <summary>
        /// Subscribes an asynchronous handler that receives <see cref="ShutdownToken"/>.
        /// Same lifetime semantics as <see cref="RegisterHandler(Func{LogEvent, Task})"/>.
        /// </summary>
        /// <param name="handler">Async handler invoked for each emitted event.</param>
        /// <returns>Subscription whose <see cref="IDisposable.Dispose"/> unregisters the handler.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="handler"/> is <c>null</c>.</exception>
        public static IDisposable RegisterHandler(Func<LogEvent, CancellationToken, Task> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            OnLogEventAsyncCancellable += handler;
            return new Subscription(() => OnLogEventAsyncCancellable -= handler);
        }

        /// <summary>
        /// Removes every registered subscriber. Does not cancel in-flight async
        /// work and does not reset <see cref="ShutdownToken"/>.
        /// </summary>
        public static void ClearHandlers()
        {
            OnLog = null;
            OnLogEvent = null;
            OnLogEventAsync = null;
            OnLogEventAsyncCancellable = null;
        }

        /// <summary>
        /// Waits for currently in-flight async handlers to complete. Tasks still
        /// running after <paramref name="timeout"/> are abandoned (process exit
        /// will tear them down). Faulted handlers do not propagate — they are
        /// already routed to <see cref="SelfLog"/>.
        /// </summary>
        /// <param name="timeout">Max wait duration. Pass <see cref="Timeout.InfiniteTimeSpan"/> to wait forever.</param>
        /// <param name="cancellationToken">Aborts the wait early. Does not cancel handlers themselves — register them with the cancellable overload to do that.</param>
        /// <returns>Task that completes when handlers finish, the timeout elapses, or the token fires.</returns>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
        public static Task FlushAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled(cancellationToken);

            if (InFlight.IsEmpty) return Task.CompletedTask;

            var snapshot = InFlight.Keys.ToArray();
            if (snapshot.Length == 0) return Task.CompletedTask;

            var allDone = Task.WhenAll(snapshot);

            if (timeout == Timeout.InfiniteTimeSpan && !cancellationToken.CanBeCanceled)
            {
                return SwallowFaults(allDone);
            }

            return WaitForCompletion(allDone, timeout, cancellationToken);
        }

        /// <summary>
        /// Cancels <see cref="ShutdownToken"/>, then drains in-flight handlers
        /// via <see cref="FlushAsync(TimeSpan, CancellationToken)"/>. After this
        /// returns, the shutdown token stays cancelled for the rest of the
        /// process — use <see cref="ResetForTests"/> only in tests.
        /// </summary>
        /// <param name="timeout">Max time to wait for handlers to drain.</param>
        /// <param name="cancellationToken">Aborts the drain early.</param>
        /// <returns>Task that completes once the drain finishes or the deadline elapses.</returns>
        public static async Task ShutdownAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            try
            {
                Volatile.Read(ref _shutdownCts).Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception ex)
            {
                SelfLog.WriteLine("GlobalEvent shutdown-token callback faulted: {0}", ex);
            }

            await FlushAsync(timeout, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Tests-only: clears handlers, drops in-flight tracking, and replaces
        /// the shutdown token source so subsequent tests start from a clean
        /// state. The previous CTS is intentionally not disposed — it has no
        /// unmanaged resources, and disposing it would race with concurrent
        /// dispatch reading <see cref="ShutdownToken"/>.
        /// </summary>
        internal static void ResetForTests()
        {
            ClearHandlers();
            InFlight.Clear();
            Interlocked.Exchange(ref _shutdownCts, new CancellationTokenSource());
        }

        private static void DispatchSync(Action<LogEvent> handlers, LogEvent e)
        {
            foreach (var entry in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<LogEvent>)entry).Invoke(e);
                }
                catch (Exception ex)
                {
                    SelfLog.WriteLine("GlobalEvent sync handler threw: {0}", ex);
                }
            }
        }

        private static void DispatchSimple(
            Action<LogEventLevel, DateTimeOffset, string> handlers,
            LogEventLevel level,
            DateTimeOffset timestamp,
            string message)
        {
            foreach (var entry in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<LogEventLevel, DateTimeOffset, string>)entry).Invoke(level, timestamp, message);
                }
                catch (Exception ex)
                {
                    SelfLog.WriteLine("GlobalEvent sync handler threw: {0}", ex);
                }
            }
        }

        private static void DispatchAsync(Func<LogEvent, Task>? handlers, LogEvent e)
        {
            if (handlers == null) return;

            foreach (var entry in handlers.GetInvocationList())
            {
                var pending = SafeInvoke((Func<LogEvent, Task>)entry, e);
                Track(pending);
            }
        }

        private static void DispatchCancellable(Func<LogEvent, CancellationToken, Task>? handlers, LogEvent e)
        {
            if (handlers == null) return;

            var ct = Volatile.Read(ref _shutdownCts).Token;
            foreach (var entry in handlers.GetInvocationList())
            {
                var pending = SafeInvoke((Func<LogEvent, CancellationToken, Task>)entry, e, ct);
                Track(pending);
            }
        }

        private static Task SafeInvoke(Func<LogEvent, Task> handler, LogEvent e)
        {
            Task? task;
            try
            {
                task = handler(e);
            }
            catch (Exception ex)
            {
                SelfLog.WriteLine("GlobalEvent async handler threw synchronously: {0}", ex);
                return Task.CompletedTask;
            }

            if (task == null) return Task.CompletedTask;

            if (task.IsCompleted)
            {
                if (task.IsFaulted)
                {
                    SelfLog.WriteLine("GlobalEvent async handler faulted: {0}", task.Exception);
                }
                return Task.CompletedTask;
            }

            return AwaitWithCatch(task);
        }

        private static Task SafeInvoke(Func<LogEvent, CancellationToken, Task> handler, LogEvent e, CancellationToken ct)
        {
            Task? task;
            try
            {
                task = handler(e, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                SelfLog.WriteLine("GlobalEvent async handler threw synchronously: {0}", ex);
                return Task.CompletedTask;
            }

            if (task == null) return Task.CompletedTask;

            if (task.IsCompleted)
            {
                if (task.IsFaulted)
                {
                    SelfLog.WriteLine("GlobalEvent async handler faulted: {0}", task.Exception);
                }
                return Task.CompletedTask;
            }

            return AwaitWithCatch(task, ct);
        }

        private static async Task AwaitWithCatch(Task task)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                SelfLog.WriteLine("GlobalEvent async handler faulted: {0}", ex);
            }
        }

        private static async Task AwaitWithCatch(Task task, CancellationToken ct)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                SelfLog.WriteLine("GlobalEvent async handler faulted: {0}", ex);
            }
        }

        private static void Track(Task task)
        {
            // Already-completed tasks (sync path or fast-completion) need no tracking.
            if (task.IsCompleted) return;

            InFlight.TryAdd(task, 0);

            // Race: the task may have completed between the check above and TryAdd.
            // If so, our ContinueWith would never fire and the entry would leak —
            // remove it eagerly here.
            if (task.IsCompleted)
            {
                InFlight.TryRemove(task, out _);
                return;
            }

            task.ContinueWith(
                RemoveFromInFlight,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private static async Task SwallowFaults(Task task)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Tracked tasks are AwaitWithCatch-wrapped and shouldn't fault here;
                // log defensively so a regression isn't silently swallowed.
                SelfLog.WriteLine("GlobalEvent flush observed faulted handler: {0}", ex);
            }
        }

        private static async Task WaitForCompletion(Task task, TimeSpan timeout, CancellationToken cancellationToken)
        {
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                var delay = timeout == Timeout.InfiniteTimeSpan
                    ? Task.Delay(Timeout.Infinite, cts.Token)
                    : Task.Delay(timeout, cts.Token);

                var winner = await Task.WhenAny(task, delay).ConfigureAwait(false);
                cts.Cancel();

                if (winner == task)
                {
                    try
                    {
                        await task.ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        SelfLog.WriteLine("GlobalEvent flush observed faulted handler: {0}", ex);
                    }
                }
                else
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
        }
    }
}
