using System;
using CoreHelpers.TaskLogging;
using Microsoft.Extensions.Logging;

namespace CoreHelpers.Extensions.Logging.Tasks
{
    internal class TaskLoggerProvider : ILoggerProvider
	{
        private ITaskLoggerFactory _taskLoggerFactory;
        private ILogger? _activeLogger = null;

        public TaskLoggerProvider(ITaskLoggerFactory taskLoggerFactory)
		{
            _taskLoggerFactory = taskLoggerFactory;
		}

        public ILogger CreateLogger(string categoryName)
        {
            if (_activeLogger == null)
                _activeLogger = new TaskLogger(_taskLoggerFactory);

            return _activeLogger;
        }

        public void Dispose()
        {
            
        }
    }
}

