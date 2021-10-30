using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions
{
    internal static class TimeSpanExtensions
    {
        private static readonly TimeSpan MinBackoff = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(10);

        /// <summary>
        /// This method calculates a random exponential backoff which lies between <see cref="MinBackoff"/> and <see cref="MaxBackoff"/>.
        /// </summary>
        /// <param name="interval">The maximum backoff to be used if <paramref name="interval"/> is less than <see cref="MaxBackoff"/>.</param>
        /// <param name="attempts">The number of attempts made to backoff.</param>
        /// <returns>The calculated exponential backoff time.</returns>
        public static TimeSpan CalculateBackoffTime(this TimeSpan interval, int attempts)
        {
            if (attempts < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(attempts), attempts, "The number of attempts should not be less than 1.");
            }

            if (interval < MinBackoff)
            {
                throw new ArgumentOutOfRangeException(nameof(interval), interval, $"The interval cannot be less than {nameof(MinBackoff)}.");
            }

            TimeSpan maxBackoff = TimeSpan.FromTicks(Math.Min(interval.Ticks, MaxBackoff.Ticks));
            TimeSpan calculatedBackoff = TimeSpan.FromTicks(MinBackoff.Ticks * new Random().Next(1, (int)Math.Min(Math.Pow(2, attempts - 1), int.MaxValue)));

            return TimeSpan.FromTicks(Math.Min(maxBackoff.Ticks, calculatedBackoff.Ticks));
        }
    }
}
