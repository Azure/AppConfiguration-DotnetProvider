// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions
{
    internal static class TimeSpanExtensions
    {
        private const int MaxAttempts = 63;

        /// <summary>
        /// This method calculates randomized exponential backoff times for operations that occur periodically on a given <paramref name="interval"/>.
        /// </summary>
        /// <param name="interval">The periodic interval at which the operation occurs. If <paramref name="interval"/> is less than <paramref name="max"/>, <paramref name="interval"/> will be the maximum backoff time..
        /// If <paramref name="interval"/> is less than <paramref name="min"/>, <paramref name="interval"/> is returned.
        /// </param>
        /// <param name="min">The minimum backoff time if <paramref name="interval"/> is greater than <paramref name="min"/>.</param>
        /// <param name="max">The maximum backoff time if <paramref name="interval"/> is greater than <paramref name="max"/>.</param>
        /// <param name="attempts">The number of attempts made to backoff.</param>
        /// <returns>The calculated exponential backoff time, or <paramref name="interval"/> if it is less than <paramref name="min"/>.</returns>
        public static TimeSpan CalculateBackoffTime(this TimeSpan interval, TimeSpan min, TimeSpan max, int attempts)
        {
            if (attempts < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(attempts), attempts, "The number of attempts should not be less than 1.");
            }

            if (interval <= min)
            {
                return interval;
            }

            max = TimeSpan.FromTicks(Math.Min(interval.Ticks, max.Ticks));

            return min.CalculateBackoffDuration(max, attempts);
        }

        /// <summary>
        /// This method calculates the randomized exponential backoff duration for the configuration store after a failure
        /// which lies between <paramref name="minDuration"/> and <paramref name="maxDuration"/>.
        /// </summary>
        /// <param name="minDuration">The minimum duration to retry after.</param>
        /// <param name="maxDuration">The maximum duration to retry after.</param>
        /// <param name="attempts">The number of attempts made to the configuration store.</param>
        /// <returns>The backoff duration before retrying a request to the configuration store or replica again.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// An exception is thrown when <paramref name="attempts"/> is less than 1.
        /// </exception>
        public static TimeSpan CalculateBackoffDuration(this TimeSpan minDuration, TimeSpan maxDuration, int attempts)
        {
            if (attempts < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(attempts), attempts, "The number of attempts should not be less than 1.");
            }

            if (attempts == 1)
            {
                return minDuration;
            }

            //
            // IMPORTANT: This can overflow
            double calculatedMilliseconds = Math.Max(1, minDuration.TotalMilliseconds) * ((long)1 << Math.Min(attempts, MaxAttempts));

            if (calculatedMilliseconds > maxDuration.TotalMilliseconds ||
                    calculatedMilliseconds <= 0 /*overflow*/)
            {
                calculatedMilliseconds = maxDuration.TotalMilliseconds;
            }

            return TimeSpan.FromMilliseconds(minDuration.TotalMilliseconds + new Random().NextDouble() * (calculatedMilliseconds - minDuration.TotalMilliseconds));
        }
    }
}
