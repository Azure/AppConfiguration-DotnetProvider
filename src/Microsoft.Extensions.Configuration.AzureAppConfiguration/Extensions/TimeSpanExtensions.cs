using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions
{
    internal static class TimeSpanExtensions
    {
        private static readonly TimeSpan DefaultBackoffDuringRefreshErrors = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan MaxBackoffDuringRefreshErrors = TimeSpan.FromMinutes(10);

        public static TimeSpan CalculateBackoffTime(this TimeSpan cacheExpirationTime, int attempts)
        {
            TimeSpan maxBackoff = cacheExpirationTime < MaxBackoffDuringRefreshErrors ? cacheExpirationTime : MaxBackoffDuringRefreshErrors;

            long ticks = DefaultBackoffDuringRefreshErrors.Ticks * new Random().Next(0, (int)Math.Min(Math.Pow(2, attempts - 1), int.MaxValue));
            TimeSpan calculatedBackoff = TimeSpan.FromTicks(Math.Max(DefaultBackoffDuringRefreshErrors.Ticks, ticks));

            return maxBackoff < calculatedBackoff ? maxBackoff : calculatedBackoff;
        }
    }
}
