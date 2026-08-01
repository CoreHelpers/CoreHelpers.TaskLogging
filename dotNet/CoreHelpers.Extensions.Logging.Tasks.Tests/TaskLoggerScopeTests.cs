using CoreHelpers.TaskLogging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CoreHelpers.Extensions.Logging.Tasks.Tests;

public sealed class TaskLoggerScopeTests
{
    [Fact]
    public void ExternalScopeProvider_EnumeratesScopesFromOuterToInner()
    {
        var provider = new LoggerExternalScopeProvider();
        var scopes = new List<string>();

        using (provider.Push("parent"))
        using (provider.Push("child"))
            provider.ForEachScope((scope, list) => list.Add((string)scope!), scopes);

        Assert.Equal(new[] { "parent", "child" }, scopes);
    }

    [Fact]
    public void LogWithoutTaskScope_IsNotWrittenToTaskLogging()
    {
        var factory = new FakeTaskLoggerFactory();
        using var services = CreateServices(factory);
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("NoScope");

        logger.LogInformation("outside");

        Assert.Empty(factory.TaskMergeCalls);
        Assert.Empty(factory.TaskStatusUpdates);
    }

    [Fact]
    public void SingleTaskScope_RoutesMessagesAndCompletionToItsTask()
    {
        var factory = CreatePersistingFactory();
        using var services = CreateServices(factory);
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("SingleScope");

        using (logger.BeginTaskScope("single", TimeSpan.FromHours(1)))
            logger.LogInformation("message");

        Assert.Contains(factory.TaskMergeCalls, call => call.TaskId == "single" && call.Messages.SequenceEqual(new[] { "message" }));
        Assert.Contains(("single", CoreHelpers.TaskLogging.TaskStatus.Running), factory.TaskStatusUpdates);
        Assert.Contains(("single", CoreHelpers.TaskLogging.TaskStatus.Succeed), factory.TaskStatusUpdates);
    }

    [Fact]
    public void ParentAndChildScopes_RouteBeforeDuringAndAfterToTheInnermostActiveTask()
    {
        var factory = CreatePersistingFactory();
        using var services = CreateServices(factory);
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("NestedScopes");
        var parent = Assert.IsAssignableFrom<ITaskLoggerScope>(logger.BeginTaskScope("parent", TimeSpan.FromHours(1)));

        logger.LogInformation("before child");
        var child = Assert.IsAssignableFrom<ITaskLoggerScope>(logger.BeginTaskScope("child", TimeSpan.FromHours(1)));
        logger.LogInformation("during child");
        child.Dispose();

        Assert.Contains(("child", CoreHelpers.TaskLogging.TaskStatus.Succeed), factory.TaskStatusUpdates);
        Assert.DoesNotContain(("parent", CoreHelpers.TaskLogging.TaskStatus.Succeed), factory.TaskStatusUpdates);

        logger.LogInformation("after child");
        parent.Dispose();

        var routedMessages = factory.TaskMergeCalls.Where(call => call.Messages.Length > 0).Select(call => (call.TaskId, call.Messages.Single())).ToArray();
        Assert.Equal(new[] { ("parent", "before child"), ("child", "during child"), ("parent", "after child") }, routedMessages);
        Assert.Contains(("parent", CoreHelpers.TaskLogging.TaskStatus.Succeed), factory.TaskStatusUpdates);
    }

    [Fact]
    public void MultipleNestedScopes_AlwaysRouteToTheInnermostActiveTask()
    {
        var factory = CreatePersistingFactory();
        using var services = CreateServices(factory);
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("MultipleNestedScopes");

        using (logger.BeginTaskScope("parent", TimeSpan.FromHours(1)))
        {
            logger.LogInformation("parent one");
            using (logger.BeginTaskScope("child", TimeSpan.FromHours(1)))
            {
                logger.LogInformation("child one");
                using (logger.BeginTaskScope("grandchild", TimeSpan.FromHours(1)))
                    logger.LogInformation("grandchild");
                logger.LogInformation("child two");
            }
            logger.LogInformation("parent two");
        }

        var taskIds = factory.TaskMergeCalls.Where(call => call.Messages.Length > 0).Select(call => call.TaskId).ToArray();
        Assert.Equal(new[] { "parent", "child", "grandchild", "child", "parent" }, taskIds);
    }

    [Fact]
    public void ChildDispose_FlushesOnlyChildBufferAndLeavesParentActive()
    {
        var factory = new FakeTaskLoggerFactory { PersistedMessageCountPerMerge = 100, PersistOnlyWhenForced = true };
        using var services = CreateServices(factory);
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("IndependentBuffers");
        var parent = Assert.IsAssignableFrom<ITaskLoggerScope>(logger.BeginTaskScope("parent", TimeSpan.FromHours(1)));
        logger.LogInformation("parent before");
        var child = Assert.IsAssignableFrom<ITaskLoggerScope>(logger.BeginTaskScope("child", TimeSpan.FromHours(1)));
        logger.LogInformation("child message");

        child.Dispose();

        Assert.Contains(factory.TaskMergeCalls, call => call.Force && call.TaskId == "child" && call.Messages.SequenceEqual(new[] { "child message" }));
        Assert.DoesNotContain(factory.TaskMergeCalls, call => call.Force && call.TaskId == "parent");
        logger.LogInformation("parent after");
        Assert.Contains(factory.TaskMergeCalls, call => !call.Force && call.TaskId == "parent" && call.Messages.SequenceEqual(new[] { "parent before", "parent after" }));

        parent.Dispose();

        Assert.Contains(factory.TaskMergeCalls, call => call.Force && call.TaskId == "parent" && call.Messages.SequenceEqual(new[] { "parent before", "parent after" }));
        Assert.Single(factory.TaskStatusUpdates, update => update == ("child", CoreHelpers.TaskLogging.TaskStatus.Succeed));
    }

    [Fact]
    public void FailedChildTask_DoesNotFailItsParent()
    {
        var factory = CreatePersistingFactory();
        using var services = CreateServices(factory);
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("FailedChild");
        var parent = Assert.IsAssignableFrom<ITaskLoggerScope>(logger.BeginTaskScope("parent", TimeSpan.FromHours(1)));
        var child = Assert.IsAssignableFrom<ITaskLoggerScope>(logger.BeginTaskScope("child", TimeSpan.FromHours(1)));

        logger.LogError(new InvalidOperationException("child failure"), "failed");
        child.Dispose();
        logger.LogInformation("parent remains active");
        parent.Dispose();

        Assert.Contains(("child", CoreHelpers.TaskLogging.TaskStatus.Failed), factory.TaskStatusUpdates);
        Assert.DoesNotContain(("parent", CoreHelpers.TaskLogging.TaskStatus.Failed), factory.TaskStatusUpdates);
        Assert.Contains(("parent", CoreHelpers.TaskLogging.TaskStatus.Succeed), factory.TaskStatusUpdates);
        Assert.Contains(factory.TaskMergeCalls, call => call.TaskId == "parent" && call.Messages.SequenceEqual(new[] { "parent remains active" }));
    }

    [Fact]
    public void ExistingChildTask_NormalDisposeSetsSucceedWithoutChangingParentStatus()
    {
        var factory = CreatePersistingFactory();
        using var services = CreateServices(factory);
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("ExplicitChild");
        var parent = Assert.IsAssignableFrom<ITaskLoggerScope>(logger.BeginTaskScope("parent", TimeSpan.FromHours(1)));
        var child = Assert.IsAssignableFrom<ITaskLoggerScope>(logger.BeginTaskScope("explicit-child", TimeSpan.FromHours(1)));

        child.Dispose();

        Assert.Contains(("explicit-child", CoreHelpers.TaskLogging.TaskStatus.Succeed), factory.TaskStatusUpdates);
        Assert.DoesNotContain(("parent", CoreHelpers.TaskLogging.TaskStatus.Succeed), factory.TaskStatusUpdates);
        parent.Dispose();
        Assert.Contains(("parent", CoreHelpers.TaskLogging.TaskStatus.Succeed), factory.TaskStatusUpdates);
    }

    [Fact]
    public void FailedChildInitialization_RestoresParentScope()
    {
        var factory = CreatePersistingFactory();
        using var services = CreateServices(factory);
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("FailedChildInitialization");
        var parent = Assert.IsAssignableFrom<ITaskLoggerScope>(logger.BeginTaskScope("parent", TimeSpan.FromHours(1)));
        factory.AnnounceException = new InvalidOperationException("announcement failed");

        Assert.Throws<AggregateException>(() => logger.BeginNewTaskScope("child-type", "source", "worker", TimeSpan.FromHours(1)));

        factory.AnnounceException = null;
        logger.LogInformation("parent after failed child");
        parent.Dispose();

        Assert.Contains(factory.TaskMergeCalls, call => call.TaskId == "parent" && call.Messages.SequenceEqual(new[] { "parent after failed child" }));
        Assert.Contains(("parent", CoreHelpers.TaskLogging.TaskStatus.Succeed), factory.TaskStatusUpdates);
    }

    [Fact]
    public void ChildFlushTimer_FlushesOnlyTheChildTask()
    {
        var timerFlushed = new ManualResetEventSlim();
        var factory = new FakeTaskLoggerFactory { PersistedMessageCountPerMerge = 100, PersistOnlyWhenForced = true };
        factory.OnTaskMerge = (force, taskId, _) =>
        {
            if (force && taskId == "child")
                timerFlushed.Set();
        };
        using var services = CreateServices(factory);
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("ChildTimer");
        var parent = Assert.IsAssignableFrom<ITaskLoggerScope>(logger.BeginTaskScope("parent", TimeSpan.FromHours(1)));
        logger.LogInformation("parent pending");
        var child = Assert.IsAssignableFrom<ITaskLoggerScope>(logger.BeginTaskScope("child", TimeSpan.FromMilliseconds(20)));
        logger.LogInformation("child pending");

        Assert.True(timerFlushed.Wait(TimeSpan.FromSeconds(2)), "The child flush timer did not fire.");
        child.Dispose();

        Assert.Contains(factory.TaskMergeCalls, call => call.Force && call.TaskId == "child" && call.Messages.SequenceEqual(new[] { "child pending" }));
        Assert.DoesNotContain(factory.TaskMergeCalls, call => call.Force && call.TaskId == "parent");
        parent.Dispose();
    }

    private static FakeTaskLoggerFactory CreatePersistingFactory()
        => new() { PersistedMessageCountPerMerge = 100 };

    private static ServiceProvider CreateServices(FakeTaskLoggerFactory factory)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITaskLoggerFactory>(factory);
        services.AddLogging(builder => builder.AddTaskLogger());
        return services.BuildServiceProvider();
    }
}