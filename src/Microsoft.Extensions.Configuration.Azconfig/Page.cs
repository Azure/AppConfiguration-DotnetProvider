namespace Microsoft.Extensions.Configuration.Azconfig
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    class Page<T>
    {
        public List<T> Items { get; set; } = new List<T>();

        public Func<Task<Page<T>>> Next { get; set; } = () => Task.FromResult(new Page<T>());

        public async Task ProcessAll(Func<Page<T>, bool> processor)
        {
            if (!processor(this))
            {

                //
                // stop processing if processor returns false
                return;
            }

            Page<T> next = await Next();

            if (next.Items.Count > 0)
            {
                await next.ProcessAll(processor);
            }
        }

        public async Task<IEnumerable<T>> GetAll()
        {
            var items = new List<T>();

            await ProcessAll(current =>
            {
                items.AddRange(current.Items);

                return true;
            });

            return items;
        }
    }
}
