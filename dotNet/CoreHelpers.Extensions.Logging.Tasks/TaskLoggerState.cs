using System;
using System.Collections.Generic;

namespace CoreHelpers.Extensions.Logging.Tasks
{
	internal class TaskLoggerState
	{
		public string TaskId { get; set; } = string.Empty;

		public string TaskType { get; set; } = string.Empty;
		public string TaskSource { get; set; } = string.Empty;
		public string TaskWorker { get; set; } = string.Empty;

		public string MetaData { get; set; } = string.Empty;
		public bool IsTaskAnnounced { get; set; } = false;
		public bool LastLogWasAnError { get; set; } = false;

		public readonly object PendingMessagesSyncRoot = new object();
		public List<string> PendingMessages = new List<string>();

		public TimeSpan CacheTimeSpan { get; set; } = TimeSpan.FromSeconds(30);
	}
}