using System;
using CoreHelpers.TaskLogging;
using Microsoft.Extensions.Logging;

namespace CoreHelpers.Extensions.Logging.Tasks
{
    internal class TaskLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        private readonly ITaskLoggerFactory _taskLoggerFactory;
        private IExternalScopeProvider? _scopeProvider;
        
        public TaskLoggerProvider(ITaskLoggerFactory taskLoggerFactory)
        {
            _taskLoggerFactory = taskLoggerFactory;
        }
        
        public void Dispose() { }

        public ILogger CreateLogger(string categoryName)
            => new TaskLogger(categoryName, _scopeProvider ?? new LoggerExternalScopeProvider(), _taskLoggerFactory);

        public void SetScopeProvider(IExternalScopeProvider scopeProvider)
            => _scopeProvider = scopeProvider;
    }
}