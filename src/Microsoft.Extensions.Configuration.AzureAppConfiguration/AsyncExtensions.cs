using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal static class AsyncExtensions
    {
        public static async Task<IEnumerable<T>> ToEnumerableAsync<T>(this IAsyncEnumerable<T> enumerable, CancellationToken cancellationToken)
        {
            var iter = enumerable.GetEnumerator();
            var items = new List<T>();

            while (await iter.MoveNext(cancellationToken))
            {
                items.Add(iter.Current);
            }

            return items;
        }
    }
}
