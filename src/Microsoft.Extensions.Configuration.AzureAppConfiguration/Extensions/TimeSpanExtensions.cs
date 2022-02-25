﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Constants;
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

            if (attempts == 1)
            {
                return min;
            }

            max = TimeSpan.FromTicks(Math.Min(interval.Ticks, max.Ticks));

            //
            // IMPORTANT: This can overflow
            double maxMilliseconds = Math.Max(1, min.TotalMilliseconds) * ((long)1 << Math.Min(attempts, MaxAttempts));

            if (maxMilliseconds > max.TotalMilliseconds ||
                    maxMilliseconds <= 0 /*overflow*/)
            {
                maxMilliseconds = max.TotalMilliseconds;
            }

            return TimeSpan.FromMilliseconds(min.TotalMilliseconds + new Random().NextDouble() * (maxMilliseconds - min.TotalMilliseconds));
        }

        /// <summary>
        /// This method calculates a random exponential time to retry the primary store after a failure
        /// which lies between <paramref name="interval"/> and <see cref="RetryConstants.DefaultMaxRetryAfter"/>.
        /// </summary>
        /// <param name="interval">The minimum interval to retry after.</param>
        /// <param name="attempts">The number of attempts made to the primary config store.</param>
        /// <returns>The calculated exponential time to retry the primary config store after a failure.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// An exception is thrown when <paramref name="attempts"/> is less than 1.
        /// </exception>
        public static TimeSpan CalculateRetryAfterTime(this TimeSpan interval, int attempts)
        {
            if (attempts < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(attempts), attempts, "The number of attempts should not be less than 1.");
            }

            TimeSpan calculatedRetryAfter = TimeSpan.FromTicks(interval.Ticks * new Random().Next(1, (int)Math.Min(Math.Pow(2, attempts - 1), int.MaxValue)));

            return TimeSpan.FromTicks(Math.Min(RetryConstants.DefaultMaxRetryAfter.Ticks, calculatedRetryAfter.Ticks));
        }
    }
}
