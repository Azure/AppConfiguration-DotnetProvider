using Azure.Core;
using Microsoft.Extensions.Logging;
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
        private readonly DnsProcessor _dnsProcessor;
        private readonly Logger _logger;

        public SrvLookupClient(Logger logger)
        {
            _dnsProcessor = new DnsProcessor();
            _logger = logger;
        }

        public async Task<IReadOnlyCollection<SrvRecord>> QueryAsync(string query, CancellationToken cancellationToken)
        {
            try
            {
                var nameServers = NameServer.ResolveNameServers();

                return await ResolveQueryAsync(nameServers, _dnsProcessor, query, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(LogHelper.QuerySrvDnsFailedErrorMessage(ex.Message));

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

            int index = 0;
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
                    if(index == servers.Count - 1)
                    {
                        throw;
                    }
                    index++; 

                    continue;
                }
            }
            
            return Enumerable.Empty<SrvRecord>().ToArray();
        }
    }
}
