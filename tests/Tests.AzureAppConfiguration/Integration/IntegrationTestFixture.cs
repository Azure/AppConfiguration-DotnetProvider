// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Core;
using Azure.Data.AppConfiguration;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.AppConfiguration;
using Azure.ResourceManager.KeyVault;
using Azure.ResourceManager.Resources;
using Azure.Security.KeyVault.Secrets;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Tests.AzureAppConfiguration
{
    /// <summary>
    /// Shares Azure clients and performs one stale-resource sweep per integration test class.
    /// </summary>
    public class IntegrationTestFixture : IAsyncLifetime
    {
        private const string AppConfigStoreName = "appconfig-dotnetprovider-integrationtest";
        private const string KeyVaultName = "keyvault-dotnetprovider";
        private const string ResourceGroupName = "dotnetprovider-integrationtest";
        private readonly List<string> _initializationLog = new List<string>();

        public DefaultAzureCredential Credential { get; } = new DefaultAzureCredential(
            new DefaultAzureCredentialOptions
            {
                ExcludeSharedTokenCacheCredential = true
            });

        public ConfigurationClient ConfigurationClient { get; private set; }

        public SecretClient SecretClient { get; private set; }

        public Uri AppConfigurationEndpoint { get; private set; }

        public Uri KeyVaultEndpoint { get; private set; }

        public IReadOnlyList<string> InitializationLog => _initializationLog;

        public async Task InitializeAsync()
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                await InitializeClientsAsync();
            }
            finally
            {
                _initializationLog.Add($"Shared Azure initialization (once per framework): {stopwatch.Elapsed}.");
            }

            stopwatch.Restart();

            try
            {
                var resources = new IntegrationTestResources(ConfigurationClient, SecretClient);
                await resources.CleanupStaleResourcesAsync(
                    DateTimeOffset.UtcNow.Subtract(TimeSpan.FromHours(3)),
                    _initializationLog.Add);
            }
            finally
            {
                _initializationLog.Add($"Shared stale-resource sweep (once per framework): {stopwatch.Elapsed}.");
            }
        }

        public Task DisposeAsync()
        {
            return Task.CompletedTask;
        }

        private async Task InitializeClientsAsync(CancellationToken cancellationToken = default)
        {
            string subscriptionId = GetCurrentSubscriptionId();
            var armClient = new ArmClient(Credential);
            SubscriptionResource subscription = armClient.GetSubscriptionResource(
                new ResourceIdentifier($"/subscriptions/{subscriptionId}"));
            ResourceGroupResource resourceGroup = await subscription.GetResourceGroups().GetAsync(
                ResourceGroupName, cancellationToken);

            AppConfigurationStoreResource appConfigStore;

            try
            {
                appConfigStore = await resourceGroup.GetAppConfigurationStores().GetAsync(
                    AppConfigStoreName, cancellationToken);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                throw new InvalidOperationException(
                    $"App Configuration store '{AppConfigStoreName}' not found in resource group '{ResourceGroupName}'. Please create it before running tests.", ex);
            }

            AppConfigurationEndpoint = new Uri(appConfigStore.Data.Endpoint);
            ConfigurationClient = new ConfigurationClient(AppConfigurationEndpoint, Credential);

            KeyVaultResource keyVault = await resourceGroup.GetKeyVaults().GetAsync(KeyVaultName, cancellationToken);
            KeyVaultEndpoint = keyVault.Data.Properties.VaultUri;
            SecretClient = new SecretClient(KeyVaultEndpoint, Credential);
        }

        private static string GetCurrentSubscriptionId()
        {
            string subscriptionId = Environment.GetEnvironmentVariable("AZURE_SUBSCRIPTION_ID");

            if (!string.IsNullOrEmpty(subscriptionId))
            {
                return subscriptionId;
            }

            string jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Integration", "appsettings.Secrets.json");

            if (!File.Exists(jsonPath))
            {
                throw new InvalidOperationException(
                    $"Subscription JSON file not found at {jsonPath}. Run the GetAzureSubscription.ps1 script first.");
            }

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(jsonPath));
            return document.RootElement.GetProperty("SubscriptionId").GetString();
        }
    }
}
