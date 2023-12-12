using System;
using CoreHelpers.TaskLogging;

namespace CoreHelpers.Extensions.Logging.Tasks
{
    internal class TaskLoggerTypedScope : ITaskLoggerTypedScope
    {
        private readonly IDisposable _innerDisposable;
        
        public TaskLoggerTypedScope(TaskLoggerState taskLoggerState, IDisposable innerDisposable)
        {
            TaskId = taskLoggerState.TaskId;
            TaskType = taskLoggerState.TaskType;
            TaskSource = taskLoggerState.TaskSource;
            TaskWorker = taskLoggerState.TaskWorker;
            
            _innerDisposable = innerDisposable;
        }
        public void Dispose()
        {
            _innerDisposable.Dispose();
        }

        public string TaskId { get; }
        public string TaskType { get; }
        public string TaskSource { get; }
        public string TaskWorker { get; }
    }
}