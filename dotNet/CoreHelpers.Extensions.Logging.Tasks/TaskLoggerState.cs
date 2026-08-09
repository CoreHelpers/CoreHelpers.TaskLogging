using System;
using System.Collections.Generic;
using CoreHelpers.TaskLogging;
using System.Text.Json.Nodes;

namespace CoreHelpers.Extensions.Logging.Tasks
{
	internal class TaskLoggerState
	{
		public string TaskId { get; set; } = string.Empty;

		public string TaskType { get; set; } = string.Empty;
		public string TaskSource { get; set; } = string.Empty;
		public string TaskWorker { get; set; } = string.Empty;

		public JsonObject Metadata { get; set; } = new JsonObject();
		public JsonObject PendingMetadata { get; } = new JsonObject();
		public bool IsTaskAnnounced { get; set; } = false;
		public bool LastLogWasAnError { get; set; } = false;
		public TaskStatus? CompletionStatus { get; set; }

		public readonly object PendingMessagesSyncRoot = new object();
		public List<string> PendingMessages = new List<string>();

		public TimeSpan CacheTimeSpan { get; set; } = TimeSpan.FromSeconds(30);
	}
}