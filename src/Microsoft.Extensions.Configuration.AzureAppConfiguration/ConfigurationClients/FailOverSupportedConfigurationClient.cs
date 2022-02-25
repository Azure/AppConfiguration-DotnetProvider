// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

using Azure;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Constants;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.ConfigurationClients
{
    internal class FailOverSupportedConfigurationClient : IConfigurationClient
    {
        private const int HttpStatusRequestThrottled = 429;

        private readonly IEnumerable<LocalConfigurationClient> clients;

        private DateTimeOffset retryPrimaryConfigClientAfter;

        private int failedRequestsToPrimaryConfigClient = 0;

        public FailOverSupportedConfigurationClient(IEnumerable<LocalConfigurationClient> clients)
        {
            if (clients == null || clients.Count() < 1)
            {
                throw new ArgumentNullException(nameof(clients));
            }

            this.retryPrimaryConfigClientAfter = DateTimeOffset.UtcNow;
            this.failedRequestsToPrimaryConfigClient = 0;
            this.clients = clients;
        }

        public Task<Response<ConfigurationSetting>> GetConfigurationSettingAsync(string key, string label = null, CancellationToken cancellationToken = default)
        {
            return ExecuteWithFailOverPolicyAsync(clients.Select((client) => new Func<Task<Response<ConfigurationSetting>>>(() => client.Client.GetConfigurationSettingAsync(key, label, cancellationToken))), cancellationToken);
        }

        public Task<Response<ConfigurationSetting>> GetConfigurationSettingAsync(ConfigurationSetting setting, bool onlyIfChanged = false, CancellationToken cancellationToken = default)
        {
            return ExecuteWithFailOverPolicyAsync(clients.Select((client) => new Func<Task<Response<ConfigurationSetting>>>(() => client.Client.GetConfigurationSettingAsync(setting, onlyIfChanged, cancellationToken))), cancellationToken);
        }

        public AsyncPageable<ConfigurationSetting> GetConfigurationSettingsAsync(SettingSelector selector, CancellationToken cancellationToken = default)
        {
            return ExecuteWithFailOverPolicy(clients.Select((client) => new Func<AsyncPageable<ConfigurationSetting>>(() => client.Client.GetConfigurationSettingsAsync(selector, cancellationToken))), cancellationToken);
        }

        public void UpdateSyncToken(Uri endpoint, string syncToken)
        {
            this.clients.Single(client => client.Endpoint.Host.Equals(endpoint.Host)).Client.UpdateSyncToken(syncToken);
        }

        private async Task<Response<T>> ExecuteWithFailOverPolicyAsync<T>(IEnumerable<Func<Task<Response<T>>>> delegates, CancellationToken cancellationToken = default)
        {
            Exception latestException = null;

            for (var i = ShouldTryPrimaryConfigStore() ? 0 : 1; i < clients.Count(); i++)
            {
                var success = false;
                var operationCanceled = false;

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var result = await delegates.ElementAt(i)().ConfigureAwait(false);
                    success = true;

                    return result;
                }
                catch (RequestFailedException e) when (e.Status == (int)HttpStatusCode.ServiceUnavailable || e.Status == HttpStatusRequestThrottled)
                {
                    latestException = e;
                    continue;
                }
                catch (AggregateException ex) when (ex.InnerExceptions?.All(e => e is RequestFailedException) == true)
                {
                    var lastException = ex.InnerExceptions.LastOrDefault() as RequestFailedException;

                    if (lastException?.Status == (int)HttpStatusCode.ServiceUnavailable || lastException?.Status == HttpStatusRequestThrottled)
                    {
                        latestException = lastException;
                        continue;
                    }

                    throw;
                }
                catch (OperationCanceledException)
                {
                    operationCanceled = true;
                    throw;
                }
                finally
                {
                    if (i == 0 && !operationCanceled)
                    {
                        UpdatePrimaryConfigStoreStatus(success);
                    }
                }
            }

            throw latestException;
        }

        private T ExecuteWithFailOverPolicy<T>(IEnumerable<Func<T>> delegates, CancellationToken cancellationToken = default)
        {
            Exception latestException = null;

            for (var i = ShouldTryPrimaryConfigStore() ? 0 : 1; i < clients.Count(); i++)
            {
                var success = false;
                var operationCanceled = false;

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var result = delegates.ElementAt(i)();
                    success = true;

                    return result;
                }
                catch (RequestFailedException e) when (e.Status == (int)HttpStatusCode.ServiceUnavailable || e.Status == HttpStatusRequestThrottled)
                {
                    latestException = e;
                    continue;
                }
                catch (AggregateException ex) when (ex.InnerExceptions?.All(e => e is RequestFailedException) == true)
                {
                    var lastException = ex.InnerExceptions.LastOrDefault() as RequestFailedException;

                    if (lastException?.Status == (int)HttpStatusCode.ServiceUnavailable ||
                        lastException?.Status == HttpStatusRequestThrottled)
                    {
                        latestException = lastException;
                        continue;
                    }

                    throw;
                }
                catch (OperationCanceledException)
                {
                    operationCanceled = true;
                    throw;
                }
                finally
                {
                    if (i == 0 && !operationCanceled)
                    {
                        UpdatePrimaryConfigStoreStatus(success);
                    }
                }
            }

            throw latestException;
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
                var retryAfterTimeout = RetryConstants.DefaultMinRetryAfter.CalculateRetryAfterTime(this.failedRequestsToPrimaryConfigClient);
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
