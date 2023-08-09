using System;
using Microsoft.Extensions.Logging;
using CoreHelpers.Extensions.Logging.Tasks;

namespace CoreHelpers.TaskLogging.Sample
{
	internal class Worker
	{
		private ILogger<Worker> _logger;
		private IEnumerable<IProcessor> _processors;
        private ITaskLoggerFactory _taskLoggerFactory;

        public Worker(ILogger<Worker> logger, IEnumerable<IProcessor> processors, ITaskLoggerFactory taskLoggerFactory)
		{
			_logger = logger;
			_processors = processors;
            _taskLoggerFactory = taskLoggerFactory;
        }

		public async Task Process()
		{           
            // execute the successprocessor
            using (_logger.BeginNewTaskScope("successjob", "q", "w"))
			{				
				await _processors.Where(p => p is ProcessorSuccess).First().Execute();								
			}

            // execute the failedprocessor
            using (_logger.BeginNewTaskScope("failedjob", "q", "w"))
            {
                await _processors.Where(p => p is ProcessorFailed).First().Execute();
            }

            // execute the succssprocesssor with announcement            
            var metaData = new Dictionary<string, string>()
            {
                { "app", "CoreHelpers.TaskLogging.Sample"},
                { "class", "Main"}
            };

            var taskId = await _taskLoggerFactory.AnnounceTask("announcejob", "q", "w", metaData);

            using (_logger.BeginTaskScope(taskId))
            {
                await _processors.Where(p => p is ProcessorSuccess).First().Execute();
            }
        }
    }
}

