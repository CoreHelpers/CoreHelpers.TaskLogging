using System;
using System.Threading;

namespace CoreHelpers.TaskLogging
{
	public static class AzureTableTimebasedKeyBuilder
	{
        private const long MaxMoment = 9007199254740991; // Number.MAX_SAFE_INTEGER
        private const string TaskKeyPrefix = "task";
        private const int EncodedMomentLength = 16;

        public static string BuildDateTimeBasedRowKey(DateTimeOffset refTime, string postfix)
        {
            return $"{TaskKeyPrefix}{MaxMoment - refTime.ToUnixTimeSeconds()}{postfix}";
        }

        internal static DateTimeOffset GetReferenceTime(string taskKey)
        {
            if (string.IsNullOrEmpty(taskKey) || !taskKey.StartsWith(TaskKeyPrefix, StringComparison.Ordinal) || taskKey.Length < TaskKeyPrefix.Length + EncodedMomentLength)
                throw new ArgumentException("The task key does not contain a valid reference time.", nameof(taskKey));

            var encodedMomentText = taskKey.Substring(TaskKeyPrefix.Length, EncodedMomentLength);
            if (!long.TryParse(encodedMomentText, out var encodedMoment))
                throw new ArgumentException("The task key does not contain a valid reference time.", nameof(taskKey));

            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(MaxMoment - encodedMoment);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                throw new ArgumentException("The task key does not contain a valid reference time.", nameof(taskKey), exception);
            }
        }
    }
}