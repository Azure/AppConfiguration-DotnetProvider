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
        private const double JitterRatio = 0.25;

        private static readonly IList<KeyValuePair<TimeSpan, TimeSpan>> StartupMaxBackoffDurationIntervals = new List<KeyValuePair<TimeSpan, TimeSpan>>
        {
            new KeyValuePair<TimeSpan, TimeSpan>(TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(5)),
            new KeyValuePair<TimeSpan, TimeSpan>(TimeSpan.FromSeconds(200), TimeSpan.FromSeconds(10)),
            new KeyValuePair<TimeSpan, TimeSpan>(TimeSpan.FromSeconds(600), FailOverConstants.MinStartupBackoffDuration),
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
            if (interval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(interval), interval, "The time interval should not be equal to or less than 0.");
            }

            if (min <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(min), min, "The minimum backoff time should not be equal to or less than 0.");
            }

            if (max < min)
            {
                throw new ArgumentOutOfRangeException(nameof(max), max, "The maximum backoff time should not be less than the minimum backoff time.");
            }

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
        /// This method calculates the jittered exponential backoff duration for the configuration store after a failure
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
            if (minDuration <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(minDuration), minDuration, "The minimum backoff time should not be equal to or less than 0.");
            }

            if (maxDuration < minDuration)
            {
                throw new ArgumentOutOfRangeException(nameof(maxDuration), maxDuration, "The maximum backoff time should not be less than the minimum backoff time.");
            }

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
            double calculatedMilliseconds = minDuration.TotalMilliseconds * ((long)1 << Math.Min(attempts, MaxAttempts));

            if (calculatedMilliseconds > maxDuration.TotalMilliseconds ||
                    calculatedMilliseconds <= 0 /*overflow*/)
            {
                calculatedMilliseconds = maxDuration.TotalMilliseconds;
            }

            return TimeSpan.FromMilliseconds(calculatedMilliseconds).Jitter(JitterRatio);
        }

        /// <summary>
        /// This method tries to get the fixed backoff duration for the elapsed startup time.
        /// </summary>
        /// <param name="startupTimeElapsed">The time elapsed since the current startup began.</param>
        /// <param name="backoff">The backoff time span if getting the fixed backoff is successful.</param>
        /// <returns>A boolean indicating if getting the fixed backoff duration was successful. Returns false
        /// if the elapsed startup time is greater than the fixed backoff window.</returns>
        public static bool TryGetFixedBackoff(this TimeSpan startupTimeElapsed, out TimeSpan backoff)
        {
            foreach (KeyValuePair<TimeSpan, TimeSpan> interval in StartupMaxBackoffDurationIntervals)
            {
                if (startupTimeElapsed < interval.Key)
                {
                    backoff = interval.Value;

                    return true;
                }
            }

            backoff = TimeSpan.Zero;

            return false;
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

            double jitter = ratio * (rand.NextDouble() * 2 - 1);

            double interval = timeSpan.TotalMilliseconds * (1 + jitter);

            return TimeSpan.FromMilliseconds(interval);
        }
    }
}
