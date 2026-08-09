using System;
using System.Text.Json.Nodes;

namespace CoreHelpers.TaskLogging
{
    public interface ITaskLoggerScope : IDisposable
    {
        string TaskId { get; }
        string TaskType { get; }
        string TaskSource { get; }
        string TaskWorker { get; }

        void MergeTaskMetadata(JsonObject metadata);

        void SetStatus(TaskStatus status);
    }
}