// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

using Azure;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class FailOverSupportedConfigurationClient : IConfigurationClient
    {
        private const int HttpStatusRequestThrottled = 429;

        private readonly IEnumerable<ConfigurationClient> clients;

        private DateTimeOffset retryPrimaryConfigClientAfter;

        private int failedRequestsToPrimaryConfigClient = 0;

        public FailOverSupportedConfigurationClient(IEnumerable<ConfigurationClient> clients)
        {
            if (clients == null)
            {
                throw new ArgumentNullException(nameof(clients));
            }

            if (clients.Count() < 1)
            {
                throw new ArgumentException($"{nameof(clients)} needs at least one ClientConfiguration.");
            }

            this.retryPrimaryConfigClientAfter = DateTimeOffset.UtcNow;
            this.failedRequestsToPrimaryConfigClient = 0;
            this.clients = clients;
        }

        public Task<Response<ConfigurationSetting>> GetConfigurationSettingAsync(string key, string label = null, CancellationToken cancellationToken = default)
        {
            return ExecuteWithFailOverPolicyAsync(clients.Select((client) => new Func<Task<Response<ConfigurationSetting>>>(() => client.GetConfigurationSettingAsync(key, label, cancellationToken))), cancellationToken);
        }

        public Task<Response<ConfigurationSetting>> GetConfigurationSettingAsync(ConfigurationSetting setting, bool onlyIfChanged = false, CancellationToken cancellationToken = default)
        {
            return ExecuteWithFailOverPolicyAsync(clients.Select((client) => new Func<Task<Response<ConfigurationSetting>>>(() => client.GetConfigurationSettingAsync(setting,  onlyIfChanged, cancellationToken))), cancellationToken);
        }

        public AsyncPageable<ConfigurationSetting> GetConfigurationSettingsAsync(SettingSelector selector, CancellationToken cancellationToken = default)
        {
            return ExecuteWithFailOverPolicy(clients.Select((client) => new Func<AsyncPageable<ConfigurationSetting>>(() => client.GetConfigurationSettingsAsync(selector, cancellationToken))), cancellationToken);
        }

        private async Task<Response<T>> ExecuteWithFailOverPolicyAsync<T>(IEnumerable<Func<Task<Response<T>>>> delegates, CancellationToken cancellationToken = default)
        {
            IList<Exception> exceptions = new List<Exception>();

            for (var i = ShouldTryPrimaryConfigStore() ? 0 : 1; i < clients.Count(); i++)
            {
                var success = false;

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var result = await delegates.ElementAt(i)();
                    success = true;

                    return result;
                }
                catch (RequestFailedException e) when (e.Status == (int)HttpStatusCode.ServiceUnavailable || e.Status == HttpStatusRequestThrottled)
                {
                    exceptions.Add(e);
                    continue;
                }
                catch (Exception e)
                {
                    exceptions.Add(e);
                    break;
                }
                finally
                {
                    if (i == 0)
                    {
                        UpdatePrimaryConfigStoreStatus(success);
                    }
                }
            }

            throw new AggregateException(exceptions);
        }

        private T ExecuteWithFailOverPolicy<T>(IEnumerable<Func<T>> delegates, CancellationToken cancellationToken = default)
        {
            IEnumerable<Exception> exceptions = new List<Exception>();

            for (var i = ShouldTryPrimaryConfigStore() ? 0 : 1; i < clients.Count(); i++)
            {
                var success = false;

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var result = delegates.ElementAt(i)();
                    success = true;

                    return result;
                }
                catch (RequestFailedException e) when (e.Status == (int)HttpStatusCode.ServiceUnavailable || e.Status == HttpStatusRequestThrottled)
                {
                    exceptions.Append(e);
                    continue;
                }
                catch (Exception e)
                {
                    exceptions.Append(e);
                    break;
                }
                finally
                {
                    if (i == 0)
                    {
                        UpdatePrimaryConfigStoreStatus(success);
                    }
                }
            }

            throw new AggregateException(exceptions);
        }

        private void UpdatePrimaryConfigStoreStatus(bool success)
        {
            if (success)
            {
                this.failedRequestsToPrimaryConfigClient = 0;
                this.retryPrimaryConfigClientAfter = DateTimeOffset.UtcNow;
            }
            else
            {
                this.failedRequestsToPrimaryConfigClient++;
                var retryAfterTimeout = RefreshConstants.DefaultMinRetryAfter.CalculateRetryAfterTime(this.failedRequestsToPrimaryConfigClient);
                this.retryPrimaryConfigClientAfter = DateTimeOffset.UtcNow.Add(retryAfterTimeout);
            }
        }

        private bool ShouldTryPrimaryConfigStore()
        {
            // We should try primary config store only if
            //     primary store is the only store available
            //  or retry timeout for primary config store has passed.
            return clients.Count() == 1 || DateTimeOffset.UtcNow >= this.retryPrimaryConfigClientAfter;
        }
    }
}
