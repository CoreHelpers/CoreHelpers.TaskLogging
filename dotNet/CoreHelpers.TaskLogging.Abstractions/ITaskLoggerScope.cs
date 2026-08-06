using System;

namespace CoreHelpers.TaskLogging
{
    public interface ITaskLoggerScope : IDisposable
    {
        string TaskId { get; }
        string TaskType { get; }
        string TaskSource { get; }
        string TaskWorker { get; }

        void SetStatus(TaskStatus status);
    }
}