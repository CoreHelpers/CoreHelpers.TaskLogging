using System;
using System.Threading;

namespace CoreHelpers.TaskLogging
{
	public static class AzureTableTimebasedKeyBuilder
	{
        public static string BuildDateTimeBasedRowKey(DateTimeOffset refTime, string postfix)
        {
            long maxMoment = 9007199254740991; // Number.MAX_SAFE_INTEGER           
            DateTime origin = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            TimeSpan diff = refTime.ToUniversalTime() - origin;
            return $"task{Convert.ToInt64(maxMoment - diff.TotalSeconds)}{postfix}";
        }        
    }
}

