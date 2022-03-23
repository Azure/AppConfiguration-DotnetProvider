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
        /// This method calculates randomized exponential backoff which lies between <see cref="RefreshConstants.DefaultMinBackoff"/> and <see cref="RefreshConstants.DefaultMaxBackoff"/>.
        /// </summary>
        /// <param name="interval">The maximum backoff to be used if <paramref name="interval"/> is less than <see cref="RefreshConstants.DefaultMaxBackoff"/>.
        /// If <paramref name="interval"/> is less than <see cref="RefreshConstants.DefaultMinBackoff"/>, <paramref name="interval"/> is returned.
        /// </param>
        /// <param name="attempts">The number of attempts made to backoff.</param>
        /// <returns>The calculated exponential backoff time, or <paramref name="interval"/> if it is less than <see cref="RefreshConstants.DefaultMinBackoff"/>.</returns>
        public static TimeSpan CalculateBackoffTime(this TimeSpan interval, int attempts)
        {
            if (attempts < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(attempts), attempts, "The number of attempts should not be less than 1.");
            }

            if (interval <= RefreshConstants.DefaultMinBackoff)
            {
                return interval;
            }

            TimeSpan min = RefreshConstants.DefaultMinBackoff;
            TimeSpan max = TimeSpan.FromTicks(Math.Min(interval.Ticks, RefreshConstants.DefaultMaxBackoff.Ticks));

            if (attempts == 1)
            {
                return min;
            }

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
    }
}
