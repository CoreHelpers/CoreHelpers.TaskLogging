using System;
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

        public static ITaskLoggerScope? BeginTaskScope(this ILogger logger, string taskId, TimeSpan? cacheTimeSpan = null)
            => BeginTypedTaskScope(logger, new TaskLoggerState() { TaskId = taskId, IsTaskAnnounced = true, CacheTimeSpan = cacheTimeSpan ?? TimeSpan.FromSeconds(30) });
        
        public static ITaskLoggerScope? BeginTaskScope(this ILogger logger, string taskId, string taskWorker, TimeSpan? cacheTimeSpan = null)
            => BeginTypedTaskScope(logger, new TaskLoggerState() { TaskId = taskId, IsTaskAnnounced = true, TaskWorker = taskWorker, CacheTimeSpan = cacheTimeSpan ?? TimeSpan.FromSeconds(30) });
        
        public static ITaskLoggerScope? BeginNewTaskScope(this ILogger logger, string taskType, string taskSource, string taskWorker, TimeSpan? cacheTimeSpan = null)
            => BeginTypedTaskScope(logger, new TaskLoggerState() { TaskId = string.Empty, TaskType = taskType, TaskSource = taskSource, TaskWorker = taskWorker, IsTaskAnnounced = false, CacheTimeSpan = cacheTimeSpan ?? TimeSpan.FromSeconds(30) });
        
        public static ITaskLoggerScope? BeginNewTaskScope(this ILogger logger, string taskType, string taskSource, string taskWorker, string metaDataString, TimeSpan? cacheTimeSpan = null)
            => BeginTypedTaskScope(logger, new TaskLoggerState() { TaskId = string.Empty, TaskType = taskType, TaskSource = taskSource, TaskWorker = taskWorker, IsTaskAnnounced = false, MetaData = metaDataString, CacheTimeSpan = cacheTimeSpan ?? TimeSpan.FromSeconds(30) });
        
        private static ITaskLoggerScope? BeginTypedTaskScope(ILogger logger, TaskLoggerState taskLoggerState)
        {
            var innerDisposable = logger.BeginScope<TaskLoggerState>(taskLoggerState);
            if (innerDisposable == null)
                return null;
            
            return new TaskLoggerScope(logger, taskLoggerState, innerDisposable);
        } 
    }
}