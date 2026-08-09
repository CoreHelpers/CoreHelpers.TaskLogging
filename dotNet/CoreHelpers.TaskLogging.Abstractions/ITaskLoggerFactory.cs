using System;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Nodes;

namespace CoreHelpers.TaskLogging
{
    public enum TaskStatus
    {
        Pending,
        Running,
        Failed,
        Succeed
    }

	public interface ITaskLoggerFactory
    {
        /// <summary>
        /// Is merging all pending messages into the given task id partition
        /// </summary>
        /// <param name="flushTime"></param>
        /// <param name="taskKey"></param>
        /// <param name="messages"></param>
        /// <returns></returns>
        Task<string[]> MergePendingMessagesIfNeeded(DateTimeOffset flushTime, bool force, string taskKey, string[] messages);

        /// <summary>
        /// Announces a new task in the state pending to the logging frameowrk. Only announced
        /// tasks can be used in a task logger by calling CreateTaskLogger
        /// </summary>
        /// <param name="taskType"></param>
        /// <param name="taskSource"></param>
        /// <param name="taskWorker"></param>
        /// <returns></returns>
        Task<string> AnnounceTask(string taskType, string taskSource, string taskWorker);

        Task<string> AnnounceTask(string taskType, string taskSource, string taskWorker, CancellationToken cancellationToken);

        /// <summary>
        /// Announces a new task with structured JSON metadata.
        /// </summary>
        /// <param name="taskType"></param>
        /// <param name="taskSource"></param>
        /// <param name="taskWorker"></param>
        /// <param name="metadata"></param>
        /// <returns></returns>
        Task<string> AnnounceTask(string taskType, string taskSource, string taskWorker, JsonObject metadata);

        Task<string> AnnounceTask(string taskType, string taskSource, string taskWorker, JsonObject metadata, CancellationToken cancellationToken);

        /// <summary>
        /// Merges metadata into a task. Values are buffered until the next task status update.
        /// Existing keys are overwritten by newer values.
        /// </summary>
        /// <param name="taskId"></param>
        /// <param name="metadata"></param>
        /// <returns></returns>
        Task MergeTaskMetadata(string taskId, JsonObject metadata);

        /// <summary>
        /// Update the task status of a given task
        /// </summary>
        /// <param name="taskId"></param>
        /// <param name="taskStatus"></param>
        /// <returns></returns>
        Task UpdateTaskStatus(string taskId, TaskStatus taskStatus);
        
        /// <summary>
        /// Update the task status of a given task including the worker
        /// </summary>
        /// <param name="taskId"></param>
        /// <param name="taskStatus"></param>
        /// <param name="taskWorker"></param>
        /// <returns></returns>
        Task UpdateTaskStatus(string taskId, TaskStatus taskStatus, string taskWorker);
        
        /// <summary>
        /// Allows to update the worker of a task, especially useful for task reassignment
        /// </summary>
        /// <param name="taskId"></param>
        /// <param name="taskWorker"></param>
        /// <returns></returns>
        Task UpdateTaskWorker(string taskId, string taskWorker);
        
        /// <summary>
        /// Resolve an external id into the task id or returns null if not found
        /// </summary>
        /// <param name="externalTaskId"></param>
        /// <returns></returns>
        Task<string?> LookupTaskIdByExternalId(string externalTaskId);

        Task<string?> LookupTaskIdByExternalId(string externalTaskId, CancellationToken cancellationToken);
        
        
        /// <summary>
        /// Registeres an external id for a given task id
        /// </summary>
        /// <param name="taskId"></param>
        /// <param name="externalTaskId"></param>
        /// <returns></returns>
        Task RegisterExternlIdForTask(string taskId, string externalTaskId);

        Task RegisterExternlIdForTask(string taskId, string externalTaskId, CancellationToken cancellationToken);
    }
}