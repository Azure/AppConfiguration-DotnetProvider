// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions
{
    internal static class TimeSpanExtensions
    {
        private const int MaxAttempts = 63;
        private const double StartupJitterRatio = 0.5;

        private static readonly IList<KeyValuePair<int, TimeSpan>> StartupMaxBackoffDurationIntervals = new List<KeyValuePair<int, TimeSpan>>
        {
            new KeyValuePair<int, TimeSpan>(100, TimeSpan.FromSeconds(5)),
            new KeyValuePair<int, TimeSpan>(200, TimeSpan.FromSeconds(10)),
            new KeyValuePair<int, TimeSpan>(600, TimeSpan.FromSeconds(30)),
        };

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

        /// <summary>
        /// This method calculates the randomized exponential backoff duration for the configuration store after a failure
        /// which lies between <paramref name="initialDuration"/> and <paramref name="maxDuration"/>.
        /// </summary>
        /// <param name="initialDuration">The minimum duration to retry after.</param>
        /// <param name="maxDuration">The maximum duration to retry after.</param>
        /// <param name="attempts">The number of attempts made to the configuration store.</param>
        /// <param name="startupStartTime">The time when the current startup began.</param>
        /// <returns>The backoff duration before retrying a request to the configuration store or replica again.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// An exception is thrown when <paramref name="attempts"/> is less than 1.
        /// </exception>
        public static TimeSpan CalculateStartupBackoffDuration(this TimeSpan initialDuration, TimeSpan maxDuration, int attempts, DateTimeOffset startupStartTime)
        {
            if (attempts < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(attempts), attempts, "The number of attempts should not be less than 1.");
            }

            if (attempts == 1)
            {
                return initialDuration;
            }

            //
            // IMPORTANT: This can overflow
            double calculatedMilliseconds = Math.Max(1, initialDuration.TotalMilliseconds) * ((long)1 << Math.Min(attempts, MaxAttempts));

            if (calculatedMilliseconds > maxDuration.TotalMilliseconds ||
                    calculatedMilliseconds <= 0 /*overflow*/)
            {
                calculatedMilliseconds = maxDuration.TotalMilliseconds;
            }

            TimeSpan calculatedDuration = TimeSpan.FromMilliseconds(calculatedMilliseconds).CalculateCurrentMaxBackoffDuration(startupStartTime);

            return calculatedDuration.Jitter(StartupJitterRatio);
        }

        private static TimeSpan Jitter(this TimeSpan timeSpan, double ratio)
        {
            if (ratio < 0 || ratio > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(ratio));
            }

            if (ratio == 0)
            {
                return timeSpan;
            }

            var rand = new Random();

            long interval = (long)Math.Abs(timeSpan.Ticks * ratio);

            long lowest = (long)(timeSpan.Ticks - interval * 0.5);

            long offset = (long)(interval * rand.NextDouble());

            return TimeSpan.FromTicks(lowest + offset);
        }

        private static TimeSpan CalculateCurrentMaxBackoffDuration(this TimeSpan calculatedTimespan, DateTimeOffset startupStartTime)
        {
            long secondsElapsedDuringStartup = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - startupStartTime.ToUnixTimeSeconds();

            TimeSpan currentMaxBackoffDuration = FailOverConstants.MaxBackoffDuration;

            foreach (KeyValuePair<int, TimeSpan> interval in StartupMaxBackoffDurationIntervals)
            {
                if (secondsElapsedDuringStartup < interval.Key)
                {
                    currentMaxBackoffDuration = interval.Value;

                    break;
                }
            }

            return calculatedTimespan > currentMaxBackoffDuration ? currentMaxBackoffDuration : calculatedTimespan;
        }
    }
}
