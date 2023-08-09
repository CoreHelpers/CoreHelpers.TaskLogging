using System;
namespace CoreHelpers.Extensions.Logging.Tasks
{
	internal class TaskLoggerScope : IDisposable
	{
        private TaskLogger _taskLogger;
        private TaskLoggerState _taskState;

        public TaskLoggerScope(TaskLoggerState taskState, TaskLogger taskLogger)
		{
            _taskLogger = taskLogger;
            _taskState = taskState;
		}

        public void Dispose()
        {
            _taskLogger.DisposeScope(_taskState, this);
        }
    }
}

