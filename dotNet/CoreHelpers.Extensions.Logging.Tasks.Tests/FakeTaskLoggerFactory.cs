using CoreHelpers.TaskLogging;
using LoggingTaskStatus = CoreHelpers.TaskLogging.TaskStatus;

namespace CoreHelpers.Extensions.Logging.Tasks.Tests;

internal sealed class FakeTaskLoggerFactory : ITaskLoggerFactory
{
    public List<string[]> MergeCalls { get; } = new();

    public List<LoggingTaskStatus> StatusUpdates { get; } = new();

    public Action<bool, string[]>? OnMerge { get; set; }

    public Task<string[]> MergePendingMessagesIfNeeded(DateTimeOffset flushTime, bool force, string taskKey, string[] messages)
    {
        MergeCalls.Add(messages.ToArray());
        OnMerge?.Invoke(force, messages);
        return Task.FromResult(messages);
    }

    public Task<string> AnnounceTask(string taskType, string taskSource, string taskWorker)
        => Task.FromResult("announced-task");

    public Task<string> AnnounceTask(string taskType, string taskSource, string taskWorker, string metaData)
        => Task.FromResult("announced-task");

    public Task<string> AnnounceTask(string taskType, string taskSource, string taskWorker, IDictionary<string, string> metaDataTyped)
        => Task.FromResult("announced-task");

    public Task UpdateTaskStatus(string taskId, LoggingTaskStatus taskStatus)
    {
        StatusUpdates.Add(taskStatus);
        return Task.CompletedTask;
    }

    public Task UpdateTaskStatus(string taskId, LoggingTaskStatus taskStatus, string taskWorker)
    {
        StatusUpdates.Add(taskStatus);
        return Task.CompletedTask;
    }

    public Task UpdateTaskWorker(string taskId, string taskWorker)
        => Task.CompletedTask;

    public Task<string?> LookupTaskIdByExternalId(string externalTaskId)
        => Task.FromResult<string?>(null);

    public Task RegisterExternlIdForTask(string taskId, string externalTaskId)
        => Task.CompletedTask;
}