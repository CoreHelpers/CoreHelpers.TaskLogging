using System;
using System.Threading;
using CoreHelpers.TaskLogging;
using Microsoft.Extensions.Logging;

namespace CoreHelpers.Extensions.Logging.Tasks
{
    internal class TaskLoggerScope : ITaskLoggerScope
    {
        private readonly ILogger _logger;
        private readonly TaskLoggerState _taskLoggerState;
        private readonly IDisposable _innerDisposable;
        private readonly Timer _flushTimer;

        public TaskLoggerScope(ILogger logger, TaskLoggerState taskLoggerState, IDisposable innerDisposable)
        {
            _logger = logger;
            _taskLoggerState = taskLoggerState;
            _innerDisposable = innerDisposable;
            
            if (!taskLoggerState.IsTaskAnnounced)
                LogLifecycleEvent("TaskScopeInitPending");
            
            LogLifecycleEvent("TaskScopeStarted");
            
            _flushTimer = new Timer((state) => {
                LogLifecycleEvent("TaskScopeFlushRequired");
            }, null, taskLoggerState.CacheTimeSpan, taskLoggerState.CacheTimeSpan);
        }
        public void Dispose()
        {
            _flushTimer.Dispose();
            try
            {
                LogLifecycleEvent("TaskScopeDisposed");
            }
            finally
            {
                _innerDisposable.Dispose();
            }
        }

        public string TaskId => _taskLoggerState.TaskId;
        public string TaskType => _taskLoggerState.TaskType;
        public string TaskSource => _taskLoggerState.TaskSource;
        public string TaskWorker => _taskLoggerState.TaskWorker;

        private void LogLifecycleEvent(string eventName)
            => _logger.Log(LogLevel.None, new EventId(0, eventName), _taskLoggerState, null, (state, exception) => string.Empty);
    }
}