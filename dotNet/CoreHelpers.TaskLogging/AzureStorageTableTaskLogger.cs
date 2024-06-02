using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CoreHelpers.TaskLogging
{
	internal class AzureStorageTableTaskLogger : BaseTaskLogger
    {
        private AzureStorageTableTaskLoggerFactory _loggerFactory;        

        public AzureStorageTableTaskLogger(string taskId, int cacheLimit, TimeSpan cacheTimespan, AzureStorageTableTaskLoggerFactory loggerFactory)
            : base(taskId, cacheLimit, cacheTimespan)
		{
            _loggerFactory = loggerFactory;            
        }

        public override async Task Flush(string taskId, IEnumerable<string> messages)
        {
            await _loggerFactory.Flush(DateTimeOffset.UtcNow, taskId, messages);            
        }
    }
}

