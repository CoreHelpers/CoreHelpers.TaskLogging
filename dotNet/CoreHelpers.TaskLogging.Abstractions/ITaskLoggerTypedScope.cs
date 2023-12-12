using System;

namespace CoreHelpers.TaskLogging
{
    public interface ITaskLoggerTypedScope : IDisposable
    {
        string TaskId { get; }
        string TaskType { get; }
        string TaskSource { get; }
        string TaskWorker { get; }
    }
}