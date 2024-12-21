using System;
using System.Collections.Generic;
using CoreHelpers.TaskLogging;
using Microsoft.Extensions.Logging;

namespace CoreHelpers.Extensions.Logging.Tasks
{
    internal class TaskLogger : ILogger
    {
        private ITaskLoggerFactory _taskLoggerFactory;

        private ITaskLogger? _currentTaskLogger = null;
        private bool _lastLogWasAnError = false;

        public TaskLogger(ITaskLoggerFactory taskLoggerFactory)
        {
            _taskLoggerFactory = taskLoggerFactory;
        }

        // By default the task logging framework does not care about
        // unknown scopes
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            // check if we are interested
            var castedState = state as TaskLoggerState;
            if (castedState == null)
                return null;

            // just in case a task logger is active flush
            if (_currentTaskLogger != null)
            {
                _currentTaskLogger.Dispose();
                _lastLogWasAnError = false;
            }

            // check if we need to announce the task
            if (!castedState.IsTaskAnnounced)
            {
                if (String.IsNullOrEmpty(castedState.MetaData))
                {
                    castedState.TaskId = _taskLoggerFactory
                        .AnnounceTask(castedState.TaskType, castedState.TaskSource, castedState.TaskWorker).GetAwaiter()
                        .GetResult();
                }
                else
                {
                    castedState.TaskId = _taskLoggerFactory
                        .AnnounceTask(castedState.TaskType, castedState.TaskSource, castedState.TaskWorker, castedState.MetaData).GetAwaiter()
                        .GetResult();
                }

                castedState.IsTaskAnnounced = true;
            }

            // set a new task logger
            _currentTaskLogger = _taskLoggerFactory.CreateTaskLogger(castedState.TaskId);

            // ensure the task is running now
            _taskLoggerFactory.UpdateTaskStatus(castedState.TaskId, TaskStatus.Running, castedState.TaskWorker).GetAwaiter().GetResult();

            // done
            return new TaskLoggerScope(castedState, this);
        }

        // All levels are eanbled
        public bool IsEnabled(LogLevel logLevel) => true;

        // allow to log 
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (_currentTaskLogger == null)
                return;

            // adjust the log level
            var taskLogLevel = TaskLogLevel.Information;
            if (logLevel == LogLevel.Error || logLevel == LogLevel.Critical)
                taskLogLevel = TaskLogLevel.Error;

            // log 
            _currentTaskLogger.Log(taskLogLevel, formatter(state, exception), exception);

            // remember the exception
            if (logLevel == LogLevel.Error)
                _lastLogWasAnError = true;
            else
                _lastLogWasAnError = false;

        }

        // ensure we write all backe
        public void DisposeScope(TaskLoggerState state, TaskLoggerScope scope)
        {
            if (_currentTaskLogger != null)
            {                                   
                // write all messages back
                _currentTaskLogger.Dispose();
                _currentTaskLogger = null;

                // ensure the task is finished now                
                _taskLoggerFactory.UpdateTaskStatus(state.TaskId, _lastLogWasAnError ? TaskStatus.Failed : TaskStatus.Succeed).GetAwaiter().GetResult();

                // reset the exception
                _lastLogWasAnError = false;
            }
        }        
    }
}

