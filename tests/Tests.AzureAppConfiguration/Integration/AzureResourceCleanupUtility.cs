using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Resources;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Tests.AzureAppConfiguration
{
    /// <summary>
    /// Utility for cleaning up Azure resources created during integration tests.
    /// Can be used both programmatically during tests or as a standalone CLI tool.
    /// </summary>
    public class AzureResourceCleanupUtility
    {
        // Resource tagging and cleanup constants
        private readonly string _resourceGroupNamePrefix;
        private readonly string _testResourceTag;

        // ARM client for Azure operations
        private readonly ArmClient _armClient;
        private readonly string _subscriptionId;

        /// <summary>
        /// Creates a new instance of the Azure resource cleanup utility
        /// </summary>
        /// <param name="armClient">Azure ARM client to use</param>
        /// <param name="subscriptionId">Subscription ID to target</param>
        /// <param name="resourceGroupNamePrefix">Prefix for identifying test resource groups</param>
        /// <param name="testResourceTag">Tag key used to identify test resources</param>
        public AzureResourceCleanupUtility(
            ArmClient armClient,
            string subscriptionId,
            string resourceGroupNamePrefix = "appconfig-test-",
            string testResourceTag = "TestResource")
        {
            _armClient = armClient ?? throw new ArgumentNullException(nameof(armClient));
            _subscriptionId = !string.IsNullOrEmpty(subscriptionId) ? subscriptionId : throw new ArgumentNullException(nameof(subscriptionId));
            _resourceGroupNamePrefix = resourceGroupNamePrefix;
            _testResourceTag = testResourceTag;
        }

        /// <summary>
        /// Creates an instance of the utility with default authentication
        /// </summary>
        /// <param name="subscriptionId">Subscription ID to target</param>
        /// <returns>A configured cleanup utility</returns>
        public static AzureResourceCleanupUtility CreateWithDefaultCredentials(string subscriptionId)
        {
            // Use DefaultAzureCredential for authentication
            var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ExcludeSharedTokenCacheCredential = true
            });

            return new AzureResourceCleanupUtility(
                new ArmClient(credential),
                subscriptionId);
        }

        /// <summary>
        /// Cleans up test resources left over from previous test runs
        /// </summary>
        /// <param name="dryRun">If true, only reports what would be deleted without actually deleting</param>
        /// <param name="progressCallback">Optional callback for reporting progress</param>
        /// <returns>Number of resource groups scheduled for deletion</returns>
        public async Task<int> CleanupStaleResources(bool dryRun = false, Action<string> progressCallback = null)
        {
            void ReportProgress(string message)
            {
                progressCallback?.Invoke(message);
            }

            ReportProgress($"Checking for leftover test resources...");
            SubscriptionResource subscription = _armClient.GetSubscriptions().Get(_subscriptionId);

            // Find all resource groups that match our naming pattern and have our test tag
            var resourceGroups = subscription.GetResourceGroups();

            int cleanedCount = 0;

            await foreach (var rgResource in resourceGroups)
            {
                // Check if this is our test resource group
                if (rgResource.Data.Name.StartsWith(_resourceGroupNamePrefix) &&
                    rgResource.Data.Tags.TryGetValue(_testResourceTag, out string isTestResource) &&
                    isTestResource == "true")
                {
                    if (dryRun)
                    {
                        ReportProgress($"[DRY RUN] Would delete test resource group: {rgResource.Data.Name}");
                        cleanedCount++;
                    }
                    else
                    {
                        ReportProgress($"Cleaning up test resource group: {rgResource.Data.Name}");

                        try
                        {
                            await rgResource.DeleteAsync(WaitUntil.Started);
                            cleanedCount++;
                        }
                        catch (RequestFailedException ex)
                        {
                            ReportProgress($"Error deleting resource group {rgResource.Data.Name}: {ex.Message}");
                        }
                        catch (InvalidOperationException ex)
                        {
                            ReportProgress($"Invalid operation deleting resource group {rgResource.Data.Name}: {ex.Message}");
                        }
                        catch (TaskCanceledException ex)
                        {
                            ReportProgress($"Timeout while deleting resource group {rgResource.Data.Name}: {ex.Message}");
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            ReportProgress($"Unauthorized access when deleting resource group {rgResource.Data.Name}: {ex.Message}");
                        }
                    }
                }
            }

            var operationType = dryRun ? "identified" : "scheduled for deletion";
            ReportProgress($"Cleanup scan complete. {cleanedCount} test resource groups {operationType}.");

            return cleanedCount;
        }
    }
}
