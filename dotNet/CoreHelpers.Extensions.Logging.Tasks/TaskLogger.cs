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
        private readonly TaskLoggerOptions _options;

        public TaskLogger(string categoryName, IExternalScopeProvider scopeProvider, ITaskLoggerFactory taskLoggerFactory, TaskLoggerOptions options)
        {
            _categoryName = categoryName;
            _scopeProvider = scopeProvider;
            _taskLoggerFactory = taskLoggerFactory;
            _options = options;
        }
        
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { 
            // check if the log level is enabled
            if (!IsEnabled(logLevel))
                return;
            
            // Lifecycle events carry their originating state so timers and disposal always
            // affect the scope that raised the event. Regular logs use the innermost scope.
            var taskLoggerState = IsLifecycleEvent(eventId) && state is TaskLoggerState lifecycleState ? lifecycleState : null;
            if (taskLoggerState == null)
            {
                var scopes = new List<object?>();
                _scopeProvider.ForEachScope((scope, list) => list.Add(scope), scopes);
                taskLoggerState = scopes.LastOrDefault(scope => scope is TaskLoggerState) as TaskLoggerState;
            }

            if (taskLoggerState == null)
                return;
            
            // check if we need to announce the task as it was not announced before
            if (eventId is { Id: 0, Name: "TaskScopeInitPending" })
            {
                if (taskLoggerState.IsTaskAnnounced && !string.IsNullOrEmpty(taskLoggerState.TaskId))
                    return;

                taskLoggerState.TaskId = _taskLoggerFactory
                    .AnnounceTask(taskLoggerState.TaskType, taskLoggerState.TaskSource, taskLoggerState.TaskWorker, taskLoggerState.Metadata).GetAwaiter()
                    .GetResult();

                taskLoggerState.IsTaskAnnounced = true;
                
                return;
            }

            // check if we need to set the task as running
            if (eventId is { Id: 0, Name: "TaskScopeStarted" })
            {
                _taskLoggerFactory.UpdateTaskStatus(taskLoggerState.TaskId, TaskStatus.Running).Wait();
                return;
            }

            // lock the messages to avoid concurrency issues
            lock (taskLoggerState.PendingMessagesSyncRoot)
            {
                // check if we received the dispose message
                if (eventId is { Id: 0, Name: "TaskScopeDisposed" })
                {
                    // at this point we need to flush if needed
                    MergePendingMessages(taskLoggerState, true, true);

                    if (taskLoggerState.PendingMetadata.Count > 0)
                    {
                        _taskLoggerFactory.MergeTaskMetadata(taskLoggerState.TaskId, taskLoggerState.PendingMetadata).GetAwaiter().GetResult();
                        taskLoggerState.PendingMetadata.Clear();
                    }
                    
                    // ensure the task is finished now                
                    var completionStatus = taskLoggerState.CompletionStatus ?? (taskLoggerState.LastLogWasAnError ? TaskStatus.Failed : TaskStatus.Succeed);
                    _taskLoggerFactory.UpdateTaskStatus(taskLoggerState.TaskId, completionStatus).GetAwaiter().GetResult();
                }
                // check if we need to flush the messages
                else if (eventId is { Id: 0, Name: "TaskScopeFlushRequired" })
                {
                    // at this point we need to flush if needed
                    MergePendingMessages(taskLoggerState, true, false);
                }
                else
                {
                    // Capture one UTC timestamp for the complete log entry, including all
                    // messages generated while rendering an exception.
                    var timestampUtc = DateTimeOffset.UtcNow;

                    if (exception != null)
                    {
                        taskLoggerState.LastLogWasAnError = true;
                        LogException(taskLoggerState, logLevel, eventId, exception, timestampUtc);
                    }
                    else
                    {
                        var message = formatter(state, exception);
                        if (message == null)
                            return;

                        AddPendingMessageAndMerge(taskLoggerState, logLevel, eventId, message, null, timestampUtc);
                    }
                }
            }
        }

        private void AddPendingMessageAndMerge(TaskLoggerState taskLoggerState, LogLevel logLevel, EventId eventId, string message, Exception? exception, DateTimeOffset timestampUtc)
        {
            // Any provider-owned message prefix (for example the memory prefix) belongs
            // in message before this context is created. The application formatter is
            // deliberately the last transformation before insertion.
            var context = new TaskLoggerMessageContext(logLevel, timestampUtc, _categoryName, eventId, message, exception);
            var persistedMessage = _options.MessageFormatter?.Invoke(context) ?? message;
            taskLoggerState.PendingMessages.Add(persistedMessage);

            MergePendingMessages(taskLoggerState, false, false);
        }

        private void MergePendingMessages(TaskLoggerState taskLoggerState, bool force, bool throwOnFailure)
        {
            var firstMerge = true;
            while (firstMerge || taskLoggerState.PendingMessages.Count > 0)
            {
                firstMerge = false;
                var pendingMessageCount = taskLoggerState.PendingMessages.Count;

                try
                {
                    taskLoggerState.PendingMessages = new List<string>(_taskLoggerFactory.MergePendingMessagesIfNeeded(DateTimeOffset.Now, force, taskLoggerState.TaskId, taskLoggerState.PendingMessages.ToArray()).GetAwaiter().GetResult());
                }
                catch when (!throwOnFailure)
                {
                    // Keep only the messages not persisted by earlier batches and retry
                    // them with the next log or timer flush.
                    return;
                }

                if (taskLoggerState.PendingMessages.Count >= pendingMessageCount)
                    return;
            }
        }
        
        private void LogException(TaskLoggerState taskLoggerState, LogLevel logLevel, EventId eventId, Exception exception, DateTimeOffset timestampUtc, bool innerException = false)
        {
            if (innerException)
                AddPendingMessageAndMerge(taskLoggerState, logLevel, eventId, $"Inner Exception: {exception.Message}", exception, timestampUtc);
            else
            {
                AddPendingMessageAndMerge(taskLoggerState, logLevel, eventId, $"Error with exception: {exception.Message}", exception, timestampUtc);
                try
                {
                    AddPendingMessageAndMerge(taskLoggerState, logLevel, eventId, "Dumping Raw-JSON-Export of the exception", exception, timestampUtc);
                    AddPendingMessageAndMerge(taskLoggerState, logLevel, eventId, JsonConvert.SerializeObject(exception), exception, timestampUtc);
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
                    AddPendingMessageAndMerge(taskLoggerState, logLevel, eventId, el, exception, timestampUtc);
            }

            if (exception.InnerException != null)
                LogException(taskLoggerState, logLevel, eventId, exception.InnerException, timestampUtc, true);
        }

        public bool IsEnabled(LogLevel logLevel)
            => true;

        private static bool IsLifecycleEvent(EventId eventId)
            => eventId.Id == 0 && (eventId.Name == "TaskScopeInitPending" || eventId.Name == "TaskScopeStarted" || eventId.Name == "TaskScopeFlushRequired" || eventId.Name == "TaskScopeDisposed");

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return _scopeProvider.Push(state);
        }
    }
}