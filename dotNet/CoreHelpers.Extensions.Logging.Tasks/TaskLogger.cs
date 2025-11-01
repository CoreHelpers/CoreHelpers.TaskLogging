using System;
using System.Collections.Generic;
using System.Linq;
using CoreHelpers.TaskLogging;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace CoreHelpers.Extensions.Logging.Tasks
{
    internal class TaskLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly IExternalScopeProvider _scopeProvider;
        private readonly ITaskLoggerFactory _taskLoggerFactory;

        public TaskLogger(string categoryName, IExternalScopeProvider scopeProvider, ITaskLoggerFactory taskLoggerFactory)
        {
            _categoryName = categoryName;
            _scopeProvider = scopeProvider;
            _taskLoggerFactory = taskLoggerFactory;
        }
        
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { 
            // check if the log level is enabled
            if (!IsEnabled(logLevel))
                return;
            
            // collect ITaskLoggerTypedScope from the scope provider
            var scopes = new List<object>();
            _scopeProvider.ForEachScope((scope, list) => list.Add(scope), scopes);

            // filter for the task logger scope, if not in the list nothing todo
            var taskLoggerState = scopes.FirstOrDefault(scope => scope is TaskLoggerState typed) as TaskLoggerState;
            if (taskLoggerState == null)
                return;
            
            // check if we need to announce the task as it was not announced before
            if (eventId is { Id: 0, Name: "TaskScopeInitPending" })
            {
                if (taskLoggerState.IsTaskAnnounced && !string.IsNullOrEmpty(taskLoggerState.TaskId))
                    return;

                if (String.IsNullOrEmpty(taskLoggerState.MetaData))
                {
                    taskLoggerState.TaskId = _taskLoggerFactory
                        .AnnounceTask(taskLoggerState.TaskType, taskLoggerState.TaskSource, taskLoggerState.TaskWorker).GetAwaiter()
                        .GetResult();
                }
                else
                {
                    taskLoggerState.TaskId = _taskLoggerFactory
                        .AnnounceTask(taskLoggerState.TaskType, taskLoggerState.TaskSource, taskLoggerState.TaskWorker, taskLoggerState.MetaData).GetAwaiter()
                        .GetResult();
                }

                taskLoggerState.IsTaskAnnounced = true;
                
                return;
            }

            // check if we need to set the task as running
            if (eventId is { Id: 0, Name: "TaskScopeStarted" })
            {
                _taskLoggerFactory.UpdateTaskStatus(taskLoggerState.TaskId, TaskStatus.Running).Wait();
                return;
            }
            
            // in case of exception, we need to log it more in details
            if (exception != null)
            {
                // set the last log as error so that the task will be marked as failed
                taskLoggerState.LastLogWasAnError = true;
                
                // render the exception
                LogException(logLevel, exception, formatter);
                return;
            }
            
            // lock the messages to avoid concurrency issues
            lock (taskLoggerState.PendingMessages)
            {
                // check if we received the dispose message
                if (eventId is { Id: 0, Name: "TaskScopeDisposed" })
                {
                    // at this point we need to flush if needed
                    taskLoggerState.PendingMessages = new List<string>(_taskLoggerFactory.MergePendingMessagesIfNeeded(DateTimeOffset.Now, true, taskLoggerState.TaskId, taskLoggerState.PendingMessages.ToArray()).GetAwaiter().GetResult());
                    
                    // ensure the task is finished now                
                    _taskLoggerFactory.UpdateTaskStatus(taskLoggerState.TaskId, taskLoggerState.LastLogWasAnError ? TaskStatus.Failed : TaskStatus.Succeed).GetAwaiter().GetResult();
                }
                // check if we need to flush the messages
                else if (eventId is { Id: 0, Name: "TaskScopeFlushRequired" })
                {
                    // at this point we need to flush if needed
                    taskLoggerState.PendingMessages = new List<string>(_taskLoggerFactory.MergePendingMessagesIfNeeded(DateTimeOffset.Now, true, taskLoggerState.TaskId, taskLoggerState.PendingMessages.ToArray()).GetAwaiter().GetResult());
                }
                else
                {
                    // get the formated message
                    var msg = formatter(state, exception);
                    if (msg == null)
                        return;
                    
                    // at this point we need to add the message to the scope
                    taskLoggerState.PendingMessages.Add(msg);

                    // at this point we need to flush if needed
                    taskLoggerState.PendingMessages = new List<string>(_taskLoggerFactory.MergePendingMessagesIfNeeded(DateTimeOffset.Now, false, taskLoggerState.TaskId, taskLoggerState.PendingMessages.ToArray()).GetAwaiter().GetResult());
                }
            }
        }
        
        private void LogException<TState>(LogLevel logLevel, Exception exception, Func<TState, Exception?, string> formatter, bool innerException = false)
        {
            if (innerException)
                this.Log(logLevel, $"Inner Exception: {exception.Message}", null, formatter);
            else
            {
                this.Log(logLevel, $"Error with exception: {exception.Message}", null, formatter);                
                try
                {
                    this.Log(logLevel, "Dumping Raw-JSON-Export of the exception", null, formatter);
                    this.Log(logLevel, JsonConvert.SerializeObject(exception), null, formatter);
                }
                catch (Exception)
                {
                    // The exception is catched without handling to prevent
                    // crashed just because of invalid JSON message we try to log
                }
            }

            if (exception.StackTrace != null)
            {
                var splittedError = exception.StackTrace.Split('\n');
                foreach (var el in splittedError)
                    this.Log(logLevel, el, null, formatter);
            }

            if (exception.InnerException != null)
                LogException(logLevel, exception.InnerException, formatter, true);
        }

        public bool IsEnabled(LogLevel logLevel)
            => true;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return _scopeProvider.Push(state);
        }
    }
}