using System;
using System.Runtime.CompilerServices;
using CoreHelpers.TaskLogging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace CoreHelpers.Extensions.Logging.Tasks
{
    public static class TaskLoggerExtension
    {
        public static ILoggingBuilder AddTaskLogger(this ILoggingBuilder builder)
        {
            builder.Services.AddSingleton<ILoggerProvider, TaskLoggerProvider>();
            return builder;
        }

        public static ITaskLoggerTypedScope? BeginTaskScope(this ILogger logger, string taskId)
            => BeginTypedTaskScope(logger, new TaskLoggerState() { TaskId = taskId, IsTaskAnnounced = true });
        
        public static ITaskLoggerTypedScope? BeginTaskScope(this ILogger logger, string taskId, string taskWorker)
            => BeginTypedTaskScope(logger, new TaskLoggerState() { TaskId = taskId, IsTaskAnnounced = true, TaskWorker = taskWorker });
        
        public static ITaskLoggerTypedScope? BeginNewTaskScope(this ILogger logger, string taskType, string taskSource, string taskWorker)
            => BeginTypedTaskScope(logger, new TaskLoggerState() { TaskId = string.Empty, TaskType = taskType, TaskSource = taskSource, TaskWorker = taskWorker, IsTaskAnnounced = false });
        
        public static ITaskLoggerTypedScope? BeginNewTaskScope(this ILogger logger, string taskType, string taskSource, string taskWorker, string metaDataString)
            => BeginTypedTaskScope(logger, new TaskLoggerState() { TaskId = string.Empty, TaskType = taskType, TaskSource = taskSource, TaskWorker = taskWorker, IsTaskAnnounced = false, MetaData = metaDataString });

        private static ITaskLoggerTypedScope? BeginTypedTaskScope(ILogger logger, TaskLoggerState taskLoggerState)
        {
            var innerDisposable = logger.BeginScope<TaskLoggerState>(taskLoggerState);
            if (innerDisposable == null)
                return null;
            
            return new TaskLoggerTypedScope(taskLoggerState, innerDisposable);
        } 
    }
}