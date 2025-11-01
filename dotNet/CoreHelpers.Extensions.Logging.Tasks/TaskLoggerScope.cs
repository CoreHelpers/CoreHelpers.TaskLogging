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
                _logger.Log(LogLevel.None, new EventId(0, "TaskScopeInitPending"), string.Empty);
            
            _logger.Log(LogLevel.None, new EventId(0, "TaskScopeStarted"),string.Empty);
            
            _flushTimer = new Timer((state) => {
                _logger.Log(LogLevel.None, new EventId(0, "TaskScopeFlushRequired"), string.Empty);
            }, null, taskLoggerState.CacheTimeSpan, taskLoggerState.CacheTimeSpan);
        }
        public void Dispose()
        {
            _flushTimer.Dispose();
            _logger.Log(LogLevel.None, new EventId(0, "TaskScopeDisposed"), string.Empty);
            _innerDisposable.Dispose();
        }

        public string TaskId => _taskLoggerState.TaskId;
        public string TaskType => _taskLoggerState.TaskType;
        public string TaskSource => _taskLoggerState.TaskSource;
        public string TaskWorker => _taskLoggerState.TaskWorker;
    }
}