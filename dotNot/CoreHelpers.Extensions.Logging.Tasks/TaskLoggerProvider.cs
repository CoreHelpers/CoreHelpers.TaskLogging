using System;
using CoreHelpers.TaskLogging;
using Microsoft.Extensions.Logging;

namespace CoreHelpers.Extensions.Logging.Tasks
{
    internal class TaskLoggerProvider : ILoggerProvider
	{
        private ITaskLoggerFactory _taskLoggerFactory;

        public TaskLoggerProvider(ITaskLoggerFactory taskLoggerFactory)
		{
            _taskLoggerFactory = taskLoggerFactory;
		}

        public ILogger CreateLogger(string categoryName)
        {
            return new TaskLogger(_taskLoggerFactory);
        }

        public void Dispose()
        {
            
        }
    }
}

