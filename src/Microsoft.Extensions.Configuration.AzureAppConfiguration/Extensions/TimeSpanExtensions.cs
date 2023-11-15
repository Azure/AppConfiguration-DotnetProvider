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

        private static readonly IList<KeyValuePair<TimeSpan, TimeSpan>> StartupMaxBackoffDurationIntervals = new List<KeyValuePair<TimeSpan, TimeSpan>>
        {
            new KeyValuePair<TimeSpan, TimeSpan>(TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(5)),
            new KeyValuePair<TimeSpan, TimeSpan>(TimeSpan.FromSeconds(200), TimeSpan.FromSeconds(10)),
            new KeyValuePair<TimeSpan, TimeSpan>(TimeSpan.FromSeconds(600), TimeSpan.FromSeconds(30)),
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
        /// This method calculates the jittered exponential backoff duration for the configuration store after a failure
        /// during startup which lies between <paramref name="minDuration"/> and <paramref name="maxDuration"/>.
        /// </summary>
        /// <param name="minDuration">The minimum duration to retry after.</param>
        /// <param name="maxDuration">The maximum duration to retry after.</param>
        /// <param name="attempts">The number of attempts made to the configuration store after the fixed retry period.</param>
        /// <param name="startupTimeElapsed">The time elapsed since the current startup began.</param>
        /// <returns>The backoff duration before retrying a request to the configuration store or replica again.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// An exception is thrown when <paramref name="attempts"/> is less than 1.
        /// </exception>
        public static TimeSpan CalculateExponentialStartupBackoffDuration(this TimeSpan minDuration, TimeSpan maxDuration, int attempts, TimeSpan startupTimeElapsed)
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

            TimeSpan maxBackoffDuration = CalculateMaxStartupBackoffDuration(startupTimeElapsed);

            TimeSpan calculatedDuration = TimeSpan.FromMilliseconds(calculatedMilliseconds);

            if (calculatedDuration > maxBackoffDuration)
            {
                calculatedDuration = maxBackoffDuration;
            }

            return calculatedDuration.Jitter(StartupJitterRatio);
        }

        /// <summary>
        /// This method calculates the fixed backoff duration for the configuration store after a failure
        /// during startup 
        /// </summary>
        /// <param name="startupTimeElapsed">The time elapsed since the current startup began.</param>
        /// <returns>The backoff duration before retrying a request to the configuration store or replica again.</returns>
        public static TimeSpan CalculateFixedStartupBackoffDuration(this TimeSpan startupTimeElapsed)
        {
            return CalculateMaxStartupBackoffDuration(startupTimeElapsed).Jitter(StartupJitterRatio);
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

            double interval = Math.Abs(timeSpan.TotalMilliseconds * ratio);

            double lowest = timeSpan.TotalMilliseconds - interval * 0.5;

            double offset = interval * rand.NextDouble();

            return TimeSpan.FromMilliseconds(lowest + offset);
        }

        private static TimeSpan CalculateMaxStartupBackoffDuration(TimeSpan startupTimeElapsed)
        {
            foreach (KeyValuePair<TimeSpan, TimeSpan> interval in StartupMaxBackoffDurationIntervals)
            {
                if (startupTimeElapsed < interval.Key)
                {
                    return interval.Value;
                }
            }

            return FailOverConstants.MaxBackoffDuration;
        }
    }
}
