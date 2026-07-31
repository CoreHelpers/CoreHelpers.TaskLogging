using CoreHelpers.TaskLogging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CoreHelpers.Extensions.Logging.Tasks.Tests;

public sealed class TaskLoggerFormatterTests
{
    [Fact]
    public void AddTaskLogger_WithoutFormatter_PersistsRawMultilineMessage()
    {
        var factory = new FakeTaskLoggerFactory();
        using var services = CreateServices(factory);
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("RawCategory");

        using (var scope = logger.BeginTaskScope("task-1", TimeSpan.FromHours(1)))
        {
            Assert.NotNull(scope);
            logger.Log(LogLevel.Information, new EventId(7, "Multiline"), "first line\nsecond line", null, static (state, _) => state);
        }

        Assert.Equal(new[] { "first line\nsecond line" }, factory.MergeCalls.Last());
    }

    [Fact]
    public void ConfiguredFormatter_ReceivesStableContextAndFormatsPersistedMessage()
    {
        var factory = new FakeTaskLoggerFactory();
        TaskLoggerMessageContext? capturedContext = null;
        var callOrder = new List<string>();
        string? messageSeenByMerge = null;
        factory.OnMerge = (force, messages) =>
        {
            if (!force)
            {
                callOrder.Add("merge");
                messageSeenByMerge = messages.Single();
            }
        };
        var before = DateTimeOffset.UtcNow;
        using var services = CreateServices(factory, options =>
        {
            options.MessageFormatter = context =>
            {
                callOrder.Add("formatter");
                capturedContext = context;
                return $"[{context.LogLevel}] [{context.TimestampUtc:O}] - {context.Message}";
            };
        });
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("ConfiguredCategory");
        var eventId = new EventId(42, "Work");

        using (var scope = logger.BeginTaskScope("task-2", TimeSpan.FromHours(1)))
        {
            Assert.NotNull(scope);
            logger.Log(LogLevel.Warning, eventId, "raw message", null, static (state, _) => state);
        }

        var after = DateTimeOffset.UtcNow;
        var context = Assert.IsType<TaskLoggerMessageContext>(capturedContext);
        Assert.Equal(LogLevel.Warning, context.LogLevel);
        Assert.Equal("ConfiguredCategory", context.CategoryName);
        Assert.Equal(eventId, context.EventId);
        Assert.Equal("raw message", context.Message);
        Assert.Null(context.Exception);
        Assert.Equal(TimeSpan.Zero, context.TimestampUtc.Offset);
        Assert.InRange(context.TimestampUtc, before, after);
        Assert.Equal($"[Warning] [{context.TimestampUtc:O}] - raw message", factory.MergeCalls.Last().Single());
        Assert.Equal(new[] { "formatter", "merge" }, callOrder);
        Assert.StartsWith("[Warning]", Assert.IsType<string>(messageSeenByMerge));
    }

    [Fact]
    public void ExceptionMessages_UseOneTimestampAndExposeExceptionToFormatter()
    {
        var factory = new FakeTaskLoggerFactory();
        var contexts = new List<TaskLoggerMessageContext>();
        using var services = CreateServices(factory, options =>
        {
            options.MessageFormatter = context =>
            {
                contexts.Add(context);
                return context.Message;
            };
        });
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("ExceptionCategory");
        var exception = CaptureException();
        var eventId = new EventId(99, "Failure");

        using (var scope = logger.BeginTaskScope("task-3", TimeSpan.FromHours(1)))
        {
            Assert.NotNull(scope);
            logger.Log(LogLevel.Error, eventId, "operation failed", exception, static (state, _) => state);
        }

        Assert.NotEmpty(contexts);
        Assert.All(contexts, context =>
        {
            Assert.Equal(contexts[0].TimestampUtc, context.TimestampUtc);
            Assert.Equal(eventId, context.EventId);
            Assert.Same(exception, context.Exception);
        });
        Assert.Contains(contexts, context => context.Message == "Error with exception: boom");
        Assert.Contains(contexts, context => context.Message.Contains(nameof(CaptureException)));
        Assert.Equal(contexts.Select(context => context.Message), factory.MergeCalls.Last());
    }

    [Fact]
    public void LifecycleEvents_AreNeitherFormattedNorPersisted()
    {
        var factory = new FakeTaskLoggerFactory();
        var formatterCalls = 0;
        using var services = CreateServices(factory, options =>
        {
            options.MessageFormatter = context =>
            {
                formatterCalls++;
                return context.Message;
            };
        });
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("LifecycleCategory");

        using (var scope = logger.BeginNewTaskScope("type", "source", "worker", TimeSpan.FromHours(1)))
        {
            Assert.NotNull(scope);
            LogLifecycleEvent(logger, "TaskScopeInitPending");
            LogLifecycleEvent(logger, "TaskScopeStarted");
            LogLifecycleEvent(logger, "TaskScopeFlushRequired");
            LogLifecycleEvent(logger, "TaskScopeDisposed");
        }

        Assert.Equal(0, formatterCalls);
        Assert.NotEmpty(factory.MergeCalls);
        Assert.All(factory.MergeCalls, messages => Assert.Empty(messages));
    }

    [Fact]
    public void FailedRegularAndTimerMerges_KeepPendingMessageWithoutInterruptingCaller()
    {
        var factory = new FakeTaskLoggerFactory();
        using var services = CreateServices(factory);
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("StorageFailureCategory");
        var scope = Assert.IsAssignableFrom<ITaskLoggerScope>(logger.BeginTaskScope("task-4", TimeSpan.FromHours(1)));
        var storageException = new InvalidOperationException("Storage unavailable");
        factory.MergeException = storageException;

        logger.Log(LogLevel.Information, new EventId(1, "Work"), "pending message", null, static (state, _) => state);
        LogLifecycleEvent(logger, "TaskScopeFlushRequired");

        factory.MergeException = null;
        scope.Dispose();

        Assert.Equal(new[] { "pending message" }, factory.MergeCalls.Last());
    }

    [Fact]
    public void FailedDisposeMerge_PropagatesAndReleasesInnerScope()
    {
        var factory = new FakeTaskLoggerFactory();
        using var services = CreateServices(factory);
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("DisposeFailureCategory");
        var scope = Assert.IsAssignableFrom<ITaskLoggerScope>(logger.BeginTaskScope("task-5", TimeSpan.FromHours(1)));
        logger.Log(LogLevel.Information, new EventId(1, "Work"), "pending message", null, static (state, _) => state);
        var storageException = new InvalidOperationException("Storage unavailable");
        factory.MergeException = storageException;

        var aggregateException = Assert.Throws<AggregateException>(() => scope.Dispose());
        Assert.Same(storageException, aggregateException.InnerException);
        Assert.DoesNotContain(CoreHelpers.TaskLogging.TaskStatus.Succeed, factory.StatusUpdates);
        Assert.DoesNotContain(CoreHelpers.TaskLogging.TaskStatus.Failed, factory.StatusUpdates);

        factory.MergeException = null;
        var mergeCallCount = factory.MergeCalls.Count;
        logger.LogInformation("outside disposed scope");
        Assert.Equal(mergeCallCount, factory.MergeCalls.Count);
    }

    private static ServiceProvider CreateServices(FakeTaskLoggerFactory factory, Action<TaskLoggerOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITaskLoggerFactory>(factory);
        services.AddLogging(builder =>
        {
            if (configure == null)
                builder.AddTaskLogger();
            else
                builder.AddTaskLogger(configure);
        });
        return services.BuildServiceProvider();
    }

    private static Exception CaptureException()
    {
        try
        {
            throw new InvalidOperationException("boom");
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static void LogLifecycleEvent(ILogger logger, string eventName)
        => logger.Log(LogLevel.None, new EventId(0, eventName), string.Empty, null, static (state, _) => state);
}