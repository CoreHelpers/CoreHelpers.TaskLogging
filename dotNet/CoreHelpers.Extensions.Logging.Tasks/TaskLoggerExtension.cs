using System;
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

        public static IDisposable? BeginTaskScope(this ILogger logger, string taskId)
            => logger.BeginScope<TaskLoggerState>(new TaskLoggerState() { TaskId = taskId, IsTaskAnnounced = true });

        public static IDisposable? BeginNewTaskScope(this ILogger logger, string taskType, string taskSource, string taskWorker)
            => logger.BeginScope<TaskLoggerState>(new TaskLoggerState() { TaskId = string.Empty, TaskType = taskType, TaskSource = taskSource, TaskWorker = taskWorker, IsTaskAnnounced = false });
    }
}