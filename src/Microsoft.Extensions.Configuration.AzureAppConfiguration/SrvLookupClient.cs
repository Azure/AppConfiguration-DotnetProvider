// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DnsClient;
using DnsClient.Protocol;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class SrvLookupClient
    {
        private readonly LookupClient _tcpLookupClient;
        private readonly LookupClient _udpLookupClient;

        private const string TcpOrigin = "_origin._tcp";
        private const string TCP = "_tcp";
        private const string Alt = "_alt";

        private static readonly TimeSpan UdpSrvQueryTimeout = TimeSpan.FromSeconds(5);

        public SrvLookupClient()
        {
            _udpLookupClient = new LookupClient(new LookupClientOptions()
            {
                UseTcpFallback = false
            });

            _tcpLookupClient = new LookupClient(new LookupClientOptions()
            {
                UseTcpOnly = true
            });
        }

        public async Task<IEnumerable<SrvRecord>> QueryAsync(string host, CancellationToken cancellationToken)
        {
            string originSrvDns = $"{TcpOrigin}.{host}";
            
            IEnumerable<SrvRecord> originRecords = await InternalQueryAsync(originSrvDns, cancellationToken).ConfigureAwait(false);

            if (originRecords == null || originRecords.Count() == 0)
            {
                return Enumerable.Empty<SrvRecord>();
            }

            SrvRecord originHostSrv = originRecords.First();

            string originHost = originHostSrv.Target.Value.TrimEnd('.');

            IEnumerable<SrvRecord> results = new SrvRecord[] { originHostSrv };


            int index = 0;

            while (true)
            {
                string altSrvDns = $"{Alt}{index}.{TCP}.{originHost}";

                IEnumerable<SrvRecord> records = await InternalQueryAsync(altSrvDns, cancellationToken).ConfigureAwait(false);

                if (records == null)
                {
                    break;
                }

                results = results.Concat(records);

                // If we get no record from _alt{i} SRV, we have reached the end of _alt* list
                if (records.Count() == 0)
                {
                    break;
                }

                index++;
            }

            return results;
        }

        private async Task<IEnumerable<SrvRecord>> InternalQueryAsync(string srvDns, CancellationToken cancellationToken)
        {
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(UdpSrvQueryTimeout);

            IDnsQueryResponse dnsResponse;

            try
            {
                dnsResponse = await _udpLookupClient.QueryAsync(srvDns, QueryType.SRV, QueryClass.IN, cts.Token).ConfigureAwait(false);
            }
            catch (DnsResponseException) 
            {
                dnsResponse = await _tcpLookupClient.QueryAsync(srvDns, QueryType.SRV, QueryClass.IN, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                dnsResponse = await _tcpLookupClient.QueryAsync(srvDns, QueryType.SRV, QueryClass.IN, cancellationToken).ConfigureAwait(false);
            }

            return dnsResponse.Answers.SrvRecords();
        }
    }
}
