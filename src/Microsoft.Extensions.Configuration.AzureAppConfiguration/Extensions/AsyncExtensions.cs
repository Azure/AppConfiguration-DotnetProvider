using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions
{
    internal static class AsyncExtensions
    {
        public static async Task<IEnumerable<T>> ToEnumerableAsync<T>(this IAsyncEnumerable<T> enumerable, CancellationToken cancellationToken)
        {
            var iter = enumerable.GetEnumerator();
            var items = new List<T>();

            while (await iter.MoveNext(cancellationToken).ConfigureAwait(false))
            {
                items.Add(iter.Current);
            }

            return items;
        }

        public static Task ParallelForEachAsync<T>(this IEnumerable<T> source, Func<T, Task> function, int maxDegreeOfParallelism)
        {
            async Task AwaitPartition(IEnumerator<T> partition)
            {
                using (partition)
                {
                    while (partition.MoveNext())
                    {
                        await function(partition.Current);
                    }
                }
            }

            return Task.WhenAll(Partitioner.Create(source).GetPartitions(maxDegreeOfParallelism).AsParallel().Select(p => AwaitPartition(p)));
        }
    }
}
