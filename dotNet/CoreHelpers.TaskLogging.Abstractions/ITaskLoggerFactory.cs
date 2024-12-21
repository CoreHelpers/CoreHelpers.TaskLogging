using System;
using System.Collections.Generic;
using System.Threading.Tasks;

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
		/// Creates a new task logger for an individual task. All subsequent loggings
		/// with this task logger will be stored in the same context
		/// </summary>
		/// <param name="uniqueTaskId"></param>
		/// <returns></returns>
		ITaskLogger CreateTaskLogger(string uniqueTaskId);

        /// <summary>
        /// Announces a new task in the state pending to the logging frameowrk. Only announced
        /// tasks can be used in a task logger by calling CreateTaskLogger
        /// </summary>
        /// <param name="taskType"></param>
        /// <param name="taskSource"></param>
        /// <param name="taskWorker"></param>
        /// <returns></returns>
        Task<string> AnnounceTask(string taskType, string taskSource, string taskWorker);

        /// <summary>
        /// Announces a new task in the state pending to the logging framework including required
        /// meta data usable in the frontend. Only announced tasks can be used in a task logger by
        /// calling CreateTaskLogger
        /// </summary>
        /// <param name="taskType"></param>
        /// <param name="taskSource"></param>
        /// <param name="taskWorker"></param>
        /// <param name="metaData"></param>
        /// <returns></returns>
        Task<string> AnnounceTask(string taskType, string taskSource, string taskWorker, string metaData);
        
        /// <summary>
        /// Announces a new task in the state pending to the logging framework including required
        /// meta data usable in the frontend. Only announced tasks can be used in a task logger by
        /// calling CreateTaskLogger
        /// </summary>
        /// <param name="taskType"></param>
        /// <param name="taskSource"></param>
        /// <param name="taskWorker"></param>
        /// <param name="metaDataTyped"></param>
        /// <returns></returns>
        Task<string> AnnounceTask(string taskType, string taskSource, string taskWorker, IDictionary<string, string> metaDataTyped);

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
    }
}

