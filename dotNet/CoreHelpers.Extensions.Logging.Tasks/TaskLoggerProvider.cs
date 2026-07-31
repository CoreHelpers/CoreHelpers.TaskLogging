using System;
using CoreHelpers.TaskLogging;
using Microsoft.Extensions.Logging;

namespace CoreHelpers.Extensions.Logging.Tasks
{
    internal class TaskLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        private readonly ITaskLoggerFactory _taskLoggerFactory;
        private readonly TaskLoggerOptions _options;
        private IExternalScopeProvider? _scopeProvider;
        
        public TaskLoggerProvider(ITaskLoggerFactory taskLoggerFactory, TaskLoggerOptions options)
        {
            _taskLoggerFactory = taskLoggerFactory;
            _options = options;
        }
        
        public void Dispose() { }

        public ILogger CreateLogger(string categoryName)
            => new TaskLogger(categoryName, _scopeProvider ?? new LoggerExternalScopeProvider(), _taskLoggerFactory, _options);

        public void SetScopeProvider(IExternalScopeProvider scopeProvider)
            => _scopeProvider = scopeProvider;
    }
}