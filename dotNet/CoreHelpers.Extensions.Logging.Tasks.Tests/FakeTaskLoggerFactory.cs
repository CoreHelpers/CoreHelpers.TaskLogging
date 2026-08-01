using CoreHelpers.TaskLogging;
using LoggingTaskStatus = CoreHelpers.TaskLogging.TaskStatus;

namespace CoreHelpers.Extensions.Logging.Tasks.Tests;

internal sealed class FakeTaskLoggerFactory : ITaskLoggerFactory
{
    public List<string[]> MergeCalls { get; } = new();

    public List<(bool Force, string TaskId, string[] Messages)> TaskMergeCalls { get; } = new();

    public List<LoggingTaskStatus> StatusUpdates { get; } = new();

    public List<(string TaskId, LoggingTaskStatus Status)> TaskStatusUpdates { get; } = new();

    public Action<bool, string[]>? OnMerge { get; set; }

    public Action<bool, string, string[]>? OnTaskMerge { get; set; }

    public Exception? MergeException { get; set; }

    public Exception? AnnounceException { get; set; }

    public Exception? StatusUpdateException { get; set; }

    public Func<int, string[], Exception?>? GetMergeException { get; set; }

    public int? PersistedMessageCountPerMerge { get; set; }

    public bool PersistOnlyWhenForced { get; set; }

    public Task<string[]> MergePendingMessagesIfNeeded(DateTimeOffset flushTime, bool force, string taskKey, string[] messages)
    {
        MergeCalls.Add(messages.ToArray());
        TaskMergeCalls.Add((force, taskKey, messages.ToArray()));
        OnMerge?.Invoke(force, messages);
        OnTaskMerge?.Invoke(force, taskKey, messages);
        var mergeException = GetMergeException?.Invoke(MergeCalls.Count, messages) ?? MergeException;
        if (mergeException != null)
            throw mergeException;

        var shouldPersist = PersistedMessageCountPerMerge.HasValue && (!PersistOnlyWhenForced || force);
        return Task.FromResult(shouldPersist ? messages.Skip(PersistedMessageCountPerMerge!.Value).ToArray() : messages);
    }

    public Task<string> AnnounceTask(string taskType, string taskSource, string taskWorker)
        => AnnounceException == null ? Task.FromResult("announced-task") : Task.FromException<string>(AnnounceException);

    public Task<string> AnnounceTask(string taskType, string taskSource, string taskWorker, CancellationToken cancellationToken)
        => Task.FromResult("announced-task");

    public Task<string> AnnounceTask(string taskType, string taskSource, string taskWorker, string metaData)
        => Task.FromResult("announced-task");

    public Task<string> AnnounceTask(string taskType, string taskSource, string taskWorker, string metaData, CancellationToken cancellationToken)
        => Task.FromResult("announced-task");

    public Task<string> AnnounceTask(string taskType, string taskSource, string taskWorker, IDictionary<string, string> metaDataTyped)
        => Task.FromResult("announced-task");

    public Task<string> AnnounceTask(string taskType, string taskSource, string taskWorker, IDictionary<string, string> metaDataTyped, CancellationToken cancellationToken)
        => Task.FromResult("announced-task");

    public Task UpdateTaskStatus(string taskId, LoggingTaskStatus taskStatus)
    {
        StatusUpdates.Add(taskStatus);
        TaskStatusUpdates.Add((taskId, taskStatus));
        return StatusUpdateException == null ? Task.CompletedTask : Task.FromException(StatusUpdateException);
    }

    public Task UpdateTaskStatus(string taskId, LoggingTaskStatus taskStatus, string taskWorker)
    {
        StatusUpdates.Add(taskStatus);
        TaskStatusUpdates.Add((taskId, taskStatus));
        return Task.CompletedTask;
    }

    public Task UpdateTaskWorker(string taskId, string taskWorker)
        => Task.CompletedTask;

    public Task<string?> LookupTaskIdByExternalId(string externalTaskId)
        => Task.FromResult<string?>(null);

    public Task<string?> LookupTaskIdByExternalId(string externalTaskId, CancellationToken cancellationToken)
        => Task.FromResult<string?>(null);

    public Task RegisterExternlIdForTask(string taskId, string externalTaskId)
        => Task.CompletedTask;

    public Task RegisterExternlIdForTask(string taskId, string externalTaskId, CancellationToken cancellationToken)
        => Task.CompletedTask;
}