using System;

namespace CoreHelpers.Extensions.Logging.Tasks
{
    /// <summary>
    /// Configures task logger message persistence.
    /// </summary>
    public sealed class TaskLoggerOptions
    {
        /// <summary>
        /// Gets or sets the application-wide formatter applied immediately before a message
        /// is added to a task's pending messages. When unset, the raw message is persisted.
        /// </summary>
        public Func<TaskLoggerMessageContext, string>? MessageFormatter { get; set; }
    }
}