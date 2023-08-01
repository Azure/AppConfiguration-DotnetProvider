using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions
{
    internal static class EnumerableExtensions
    {
        public static IEnumerable<T> Shuffle<T>(this IEnumerable<T> values)
        {
            var rng = new Random();
            T[] elements = values.ToArray();

            for (int i = elements.Length - 1; i >= 0; i--)
            {
                int swapIndex = rng.Next(i + 1);

                yield return elements[swapIndex];

                elements[swapIndex] = elements[i];
            }
        }
    }
}
