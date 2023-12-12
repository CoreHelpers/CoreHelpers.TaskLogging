using System;
using Microsoft.Extensions.Logging;
using CoreHelpers.Extensions.Logging.Tasks;

namespace CoreHelpers.TaskLogging.Sample
{
	internal class Worker
	{
		private readonly ILogger<Worker> _logger;
		private readonly IEnumerable<IProcessor> _processors;
        private readonly ITaskLoggerFactory _taskLoggerFactory;

        public Worker(ILogger<Worker> logger, IEnumerable<IProcessor> processors, ITaskLoggerFactory taskLoggerFactory)
		{
			_logger = logger;
			_processors = processors;
            _taskLoggerFactory = taskLoggerFactory;
        }

		public async Task Process()
		{           
            // execute the success processor
            using (_logger.BeginNewTaskScope("successjob", "q", "w"))
			{				
				await _processors.Where(p => p is ProcessorSuccess).First().Execute();								
			}

            // execute the failedprocessor
            using (var scope = _logger.BeginNewTaskScope("failedjob", "q", "w", "app=CoreHelpers.TaskLogging.Sample,class=Main"))
            {
	            Console.WriteLine(scope.TaskId);
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

