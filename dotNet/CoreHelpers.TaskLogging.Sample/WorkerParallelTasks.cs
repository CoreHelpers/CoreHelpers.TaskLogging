using System;
using Microsoft.Extensions.Logging;
using CoreHelpers.Extensions.Logging.Tasks;
using Microsoft.Extensions.Hosting;

namespace CoreHelpers.TaskLogging.Sample
{
	internal class WorkerParallelTasks
	{
		private readonly ILogger<WorkerParallelTasks> _logger;
		private readonly IEnumerable<IProcessor> _processors;
        private readonly ITaskLoggerFactory _taskLoggerFactory;
        private readonly IHostApplicationLifetime _appLifetime;
        
        public WorkerParallelTasks(ILogger<WorkerParallelTasks> logger, IEnumerable<IProcessor> processors, ITaskLoggerFactory taskLoggerFactory, IHostApplicationLifetime appLifetime)
		{
			_logger = logger;
			_processors = processors;
            _taskLoggerFactory = taskLoggerFactory;
            _appLifetime = appLifetime;
		}

		public async Task Process()
		{           
			// spawn some parallel tasks
				_logger.LogInformation("Spawning parallel tasks");
			var tasks = new List<Task>();
			for (int i = 0; i < 5; i++) 
				tasks.Add(SpawnTask(i));
			
			// wait for all tasks to be completed
			_logger.LogInformation("Waiting for task completion");
			await Task.WhenAll(tasks);
			_logger.LogInformation("All tasks completed");
        }

		private Task SpawnTask(int iTaskNumber)
		{
			return Task.Run(async () =>
			{
				_logger.LogInformation($"Executing task {iTaskNumber}");

				// create a task logger scope
				using var _ =
					iTaskNumber == 0 ?
						_logger.BeginNewTaskScopeWithExternalId(Guid.NewGuid().ToString(), "SpawnedTask", "WorkerParallelTasks", $"TaskWorker-{iTaskNumber}", string.Empty, TimeSpan.FromSeconds(1)) :
						_logger.BeginNewTaskScope("SpawnedTask", "WorkerParallelTasks", $"TaskWorker-{iTaskNumber}", TimeSpan.FromSeconds(1));
					
				// log something in the context of the task
				_logger.LogInformation($"T{iTaskNumber}: Started task {iTaskNumber}");
				
				await Task.Delay(1000);
				_logger.LogInformation($"T{iTaskNumber}: Turn 01");
				
				await Task.Delay(1000);
				_logger.LogInformation($"T{iTaskNumber}: Turn 02");
				
				await Task.Delay(1000);
				_logger.LogInformation($"T{iTaskNumber}: Turn 03");
				
				await Task.Delay(1000);
				_logger.LogInformation($"T{iTaskNumber}: Turn 04");
				
				await Task.Delay(1000);
				_logger.LogInformation($"T{iTaskNumber}: Turn 05");

				if (iTaskNumber == 2)
					_logger.LogCritical(new Exception("Hello World"), "Hello World");
				
				_logger.LogInformation($"T{iTaskNumber}: Executed task {iTaskNumber}");
			});
		}
    }
}

