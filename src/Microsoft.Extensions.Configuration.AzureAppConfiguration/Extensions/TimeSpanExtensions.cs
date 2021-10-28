using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions
{
    internal static class TimeSpanExtensions
    {
        private static readonly TimeSpan MinBackoff = TimeSpan.FromSeconds(1);

        /// <summary>
        /// This method calculates a random exponential backoff which lies between <see cref="MinBackoff"/> and the passed value of maxBackoff.
        /// </summary>
        /// <param name="maxBackoff">The maximum backoff time.</param>
        /// <param name="attempts">The number of attempts made to backoff.</param>
        /// <returns>The calculated exponential backoff time.</returns>
        public static TimeSpan CalculateBackoffTime(this TimeSpan maxBackoff, int attempts)
        {
            long ticks = MinBackoff.Ticks * new Random().Next(0, (int)Math.Min(Math.Pow(2, attempts - 1), int.MaxValue));
            TimeSpan calculatedBackoff = TimeSpan.FromTicks(Math.Max(MinBackoff.Ticks, ticks));

            return TimeSpan.FromTicks(Math.Min(maxBackoff.Ticks, calculatedBackoff.Ticks));
        }
    }
}
