// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DnsClient;
using DnsClient.Protocol;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Constants;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class SrvLookupClient
    {
        private class OriginHostCacheItem
        {
            public string OriginHost { get; set; }

            public DateTimeOffset CacheExpires { get; set; }
        }

        private readonly ConcurrentDictionary<string, OriginHostCacheItem> _cachedOriginHosts;
        private readonly LookupClient _tcpLookupClient;
        private readonly LookupClient _udpLookupClient;

        private const string TcpOrigin = "_origin._tcp";
        private const string TCP = "_tcp";
        private const string Alt = "_alt";
        private const int MaxSrvRecordCountPerRecordSet = 20;

        private static readonly TimeSpan OriginHostResultCacheExpiration = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan UdpSrvQueryTimeout = TimeSpan.FromSeconds(5);

        public SrvLookupClient()
        {
            _cachedOriginHosts = new ConcurrentDictionary<string, OriginHostCacheItem>();

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
            IEnumerable<SrvRecord> resultRecords = Enumerable.Empty<SrvRecord>();
            var originSrvDns = $"{TcpOrigin}.{host}";

            bool exists = _cachedOriginHosts.TryGetValue(originSrvDns, out OriginHostCacheItem originHost);

            if (!exists || originHost.CacheExpires <= DateTimeOffset.UtcNow)
            {
                IEnumerable<SrvRecord> records = await InternalQueryAsync(originSrvDns, cancellationToken).ConfigureAwait(false);

                if (records == null || records.Count() == 0)
                {
                    return Enumerable.Empty<SrvRecord>();
                }

                if (!exists)
                {
                    originHost = new OriginHostCacheItem()
                    {
                        OriginHost = records.First().Target.Value.Trim('.'),
                        CacheExpires = DateTimeOffset.UtcNow.Add(OriginHostResultCacheExpiration)
                    };

                    _cachedOriginHosts[originSrvDns] = originHost;
                }
                else
                {
                    originHost.OriginHost = records.First().Target.Value.Trim('.');
                    originHost.CacheExpires = DateTimeOffset.UtcNow.Add(OriginHostResultCacheExpiration);
                }
            }

            int index = 0;

            while (true)
            {
                string altSrvDns = $"{Alt}{index}.{TCP}.{originHost.OriginHost}";

                IEnumerable<SrvRecord> records = await InternalQueryAsync(altSrvDns, cancellationToken).ConfigureAwait(false);

                if (records == null)
                {
                    break;
                }

                resultRecords = resultRecords.Concat(records);

                // If we get less than 20 records from _alt{i} SRV, we have reached the end of _alt* list
                if (records.Count() < MaxSrvRecordCountPerRecordSet)
                {
                    break;
                }

                index++;
            }

            return resultRecords;
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
