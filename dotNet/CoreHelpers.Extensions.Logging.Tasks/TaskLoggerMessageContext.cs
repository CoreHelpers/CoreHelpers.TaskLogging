using System;
using Microsoft.Extensions.Logging;

namespace CoreHelpers.Extensions.Logging.Tasks
{
    /// <summary>
    /// Describes a message immediately before it is added to a task's pending messages.
    /// </summary>
    public sealed class TaskLoggerMessageContext
    {
        public TaskLoggerMessageContext(LogLevel logLevel, DateTimeOffset timestampUtc, string categoryName, EventId eventId, string message, Exception? exception)
        {
            LogLevel = logLevel;
            TimestampUtc = timestampUtc;
            CategoryName = categoryName ?? throw new ArgumentNullException(nameof(categoryName));
            EventId = eventId;
            Message = message ?? throw new ArgumentNullException(nameof(message));
            Exception = exception;
        }

        public LogLevel LogLevel { get; }

        public DateTimeOffset TimestampUtc { get; }

        public string CategoryName { get; }

        public EventId EventId { get; }

        public string Message { get; }

        public Exception? Exception { get; }
    }
}