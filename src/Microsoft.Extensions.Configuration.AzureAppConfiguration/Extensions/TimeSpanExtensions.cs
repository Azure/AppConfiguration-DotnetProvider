// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions
{
    internal static class TimeSpanExtensions
    {
        /// <summary>
        /// This method calculates a random exponential backoff which lies between <see cref="RefreshConstants.DefaultCacheExpirationInterval"/> and <see cref="RefreshConstants.DefaultMaxBackoff"/>.
        /// </summary>
        /// <param name="interval">The maximum backoff to be used if <paramref name="interval"/> is less than <see cref="RefreshConstants.DefaultMaxBackoff"/>.
        /// If <paramref name="interval"/> is less than <see cref="RefreshConstants.DefaultCacheExpirationInterval"/>, <paramref name="interval"/> is returned.
        /// </param>
        /// <param name="attempts">The number of attempts made to backoff.</param>
        /// <returns>The calculated exponential backoff time, or <paramref name="interval"/> if it is less than <see cref="RefreshConstants.DefaultCacheExpirationInterval"/>.</returns>
        public static TimeSpan CalculateBackoffTime(this TimeSpan interval, int attempts)
        {
            if (attempts < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(attempts), attempts, "The number of attempts should not be less than 1.");
            }

            if (interval < RefreshConstants.DefaultCacheExpirationInterval)
            {
                return interval;
            }

            TimeSpan maxBackoff = TimeSpan.FromTicks(Math.Min(interval.Ticks, RefreshConstants.DefaultMaxBackoff.Ticks));
            TimeSpan calculatedBackoff = TimeSpan.FromTicks(RefreshConstants.DefaultCacheExpirationInterval.Ticks * new Random().Next(1, (int)Math.Min(Math.Pow(2, attempts - 1), int.MaxValue)));

            return TimeSpan.FromTicks(Math.Min(maxBackoff.Ticks, calculatedBackoff.Ticks));
        }
    }
}
