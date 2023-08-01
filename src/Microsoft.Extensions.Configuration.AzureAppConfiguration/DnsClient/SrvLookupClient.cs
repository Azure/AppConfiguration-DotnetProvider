using Azure.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.DnsClient
{
    internal class SrvLookupClient
    {
        private readonly DnsProcessor dnsProcessor;

        public SrvLookupClient()
        {
            dnsProcessor = new DnsProcessor();
        }

        public async Task<IReadOnlyCollection<SrvRecord>> QueryAsync(string query, CancellationToken cancellationToken)
        {
            try
            {
                var nameServers = NameServer.ResolveNameServers();
                return await ResolveQueryAsync(nameServers, dnsProcessor, query, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                //TODO: log exception
                return Enumerable.Empty<SrvRecord>().ToArray();
            }
        }

        private async Task<IReadOnlyCollection<SrvRecord>> ResolveQueryAsync(
            IReadOnlyCollection<NameServer> servers,
            DnsProcessor processor,
            string query,
            CancellationToken cancellationToken)
        {
            if (servers == null)
            {
                throw new ArgumentNullException(nameof(servers));
            }
            if (processor == null)
            {
                throw new ArgumentNullException(nameof(processor));
            }
            if (string.IsNullOrEmpty(query))
            {
                throw new ArgumentNullException(nameof(query));
            }

            foreach (var server in servers)
            {
                try
                {
                    var srvRecords = await processor.QueryAsync(
                                       server.IPEndPoint,
                                       query,
                                       cancellationToken).ConfigureAwait(false);

                    return srvRecords;
                }
                catch (Exception)
                {
                    // TODO: Log exception
                    continue;
                }
            }

            return Enumerable.Empty<SrvRecord>().ToArray(); 
        }
    }
}
