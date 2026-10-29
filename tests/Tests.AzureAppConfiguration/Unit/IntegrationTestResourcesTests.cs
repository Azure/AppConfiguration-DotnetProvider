// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Data.AppConfiguration;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Tests.AzureAppConfiguration
{
    public class IntegrationTestResourcesTests
    {
        [Fact]
        public async Task CleanupTracksLabelsFeatureFlagsSnapshotsAndSecretVersions()
        {
            Mock<ConfigurationClient> client = CreateConfigurationClient();
            Mock<SecretClient> secrets = CreateSecretClient();
            var resources = new IntegrationTestResources(client.Object, secrets.Object);
            var output = new List<string>();
            string key = "IntegrationTest-settings";
            string flag = FeatureManagementConstants.FeatureFlagMarker + "IntegrationTest-flag";

            await resources.SetConfigurationSettingAsync(new ConfigurationSetting(key, "first"));
            await resources.SetConfigurationSettingAsync(new ConfigurationSetting(key, "updated"));
            await resources.SetConfigurationSettingAsync(new ConfigurationSetting(key, "labeled", "KeyVaultRef"));
            await resources.SetConfigurationSettingAsync(new ConfigurationSetting(flag, "{}"));
            await resources.CreateSnapshotAsync("snapshot-IntegrationTest-test", CreateSnapshot());
            await resources.SetSecretAsync("IntegrationTest-secret", "first");
            await resources.SetSecretAsync("IntegrationTest-secret", "updated");

            await resources.CleanupAsync(output.Add);
            await resources.CleanupAsync(output.Add);

            client.Verify(c => c.DeleteConfigurationSettingAsync(key, null, It.IsAny<CancellationToken>()), Times.Once());
            client.Verify(c => c.DeleteConfigurationSettingAsync(key, "KeyVaultRef", It.IsAny<CancellationToken>()), Times.Once());
            client.Verify(c => c.DeleteConfigurationSettingAsync(flag, null, It.IsAny<CancellationToken>()), Times.Once());
            client.Verify(c => c.DeleteConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
            client.Verify(c => c.ArchiveSnapshotAsync("snapshot-IntegrationTest-test", It.IsAny<CancellationToken>()), Times.Once());
            secrets.Verify(c => c.StartDeleteSecretAsync("IntegrationTest-secret", It.IsAny<CancellationToken>()), Times.Once());
            Assert.Contains(output, message => message.Contains("3 settings/feature flags, 1 snapshots, 1 secrets; 0 resources remaining"));
        }

        [Theory]
        [InlineData("setting")]
        [InlineData("snapshot")]
        [InlineData("secret")]
        public async Task FailedWritesRemainTracked(string resourceType)
        {
            Mock<ConfigurationClient> client = CreateConfigurationClient();
            Mock<SecretClient> secrets = CreateSecretClient();
            var resources = new IntegrationTestResources(client.Object, secrets.Object);
            var output = new List<string>();
            var failure = new RequestFailedException(503, "The response was lost.");

            switch (resourceType)
            {
                case "setting":
                    client.Setup(c => c.SetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), false, It.IsAny<CancellationToken>()))
                        .ThrowsAsync(failure);
                    await Assert.ThrowsAsync<RequestFailedException>(() =>
                        resources.SetConfigurationSettingAsync(new ConfigurationSetting("IntegrationTest-failed", "value")));
                    break;
                case "snapshot":
                    client.Setup(c => c.CreateSnapshotAsync(WaitUntil.Completed, It.IsAny<string>(), It.IsAny<ConfigurationSnapshot>(), It.IsAny<CancellationToken>()))
                        .ThrowsAsync(failure);
                    await Assert.ThrowsAsync<RequestFailedException>(() =>
                        resources.CreateSnapshotAsync("snapshot-IntegrationTest-failed", CreateSnapshot()));
                    break;
                case "secret":
                    secrets.Setup(c => c.SetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .ThrowsAsync(failure);
                    await Assert.ThrowsAsync<RequestFailedException>(() => resources.SetSecretAsync("IntegrationTest-failed", "value"));
                    break;
                default:
                    throw new ArgumentException("Unknown resource type.", nameof(resourceType));
            }

            await resources.CleanupAsync(output.Add);

            client.Verify(c => c.DeleteConfigurationSettingAsync("IntegrationTest-failed", null, It.IsAny<CancellationToken>()),
                resourceType == "setting" ? Times.Once() : Times.Never());
            client.Verify(c => c.ArchiveSnapshotAsync("snapshot-IntegrationTest-failed", It.IsAny<CancellationToken>()),
                resourceType == "snapshot" ? Times.Once() : Times.Never());
            secrets.Verify(c => c.StartDeleteSecretAsync("IntegrationTest-failed", It.IsAny<CancellationToken>()),
                resourceType == "secret" ? Times.Once() : Times.Never());
        }

        [Theory]
        [InlineData(404)]
        [InlineData(403)]
        [InlineData(503)]
        public async Task CleanupAttemptsAllResourcesAndReportsFailures(int status)
        {
            Mock<ConfigurationClient> client = CreateConfigurationClient();
            Mock<SecretClient> secrets = CreateSecretClient();
            var resources = new IntegrationTestResources(client.Object, secrets.Object);
            var output = new List<string>();

            foreach (string suffix in new[] { "failed", "other" })
            {
                await resources.SetConfigurationSettingAsync(new ConfigurationSetting("IntegrationTest-" + suffix, "value"));
                await resources.CreateSnapshotAsync("snapshot-IntegrationTest-" + suffix, CreateSnapshot());
                await resources.SetSecretAsync("IntegrationTest-" + suffix, "value");
            }

            client.Setup(c => c.DeleteConfigurationSettingAsync("IntegrationTest-failed", null, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new RequestFailedException(status, "Setting deletion failed."));
            client.Setup(c => c.ArchiveSnapshotAsync("snapshot-IntegrationTest-failed", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new RequestFailedException(status, "Snapshot archival failed."));
            secrets.Setup(c => c.StartDeleteSecretAsync("IntegrationTest-failed", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new RequestFailedException(status, "Secret deletion failed."));

            if (status == 404)
            {
                await resources.CleanupAsync(output.Add);
                Assert.Equal(3, output.Count(message => message.StartsWith("Resource already absent:", StringComparison.Ordinal)));
            }
            else
            {
                AggregateException exception = await Assert.ThrowsAsync<AggregateException>(() => resources.CleanupAsync(output.Add));
                Assert.Equal(3, exception.InnerExceptions.Count);
                Assert.Equal(3, output.Count(message => message.StartsWith("Failed to", StringComparison.Ordinal)));
                Assert.Contains(output, message => message.Contains("3 resources remaining"));
            }

            client.Verify(c => c.DeleteConfigurationSettingAsync("IntegrationTest-other", null, It.IsAny<CancellationToken>()), Times.Once());
            client.Verify(c => c.ArchiveSnapshotAsync("snapshot-IntegrationTest-other", It.IsAny<CancellationToken>()), Times.Once());
            secrets.Verify(c => c.StartDeleteSecretAsync("IntegrationTest-failed", It.IsAny<CancellationToken>()), Times.Once());
            secrets.Verify(c => c.StartDeleteSecretAsync("IntegrationTest-other", It.IsAny<CancellationToken>()), Times.Once());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task CleanupRetriesSecretDeletionConflicts(bool alreadyDeleted)
        {
            Mock<ConfigurationClient> client = CreateConfigurationClient();
            Mock<SecretClient> secrets = CreateSecretClient();
            var resources = new IntegrationTestResources(client.Object, secrets.Object);
            var output = new List<string>();
            var operation = new Mock<DeleteSecretOperation>(MockBehavior.Strict);
            operation.Setup(o => o.WaitForCompletionAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<Response<DeletedSecret>>(Response.FromValue(
                    SecretModelFactory.DeletedSecret(new SecretProperties("IntegrationTest-secret")), Mock.Of<Response>())));

            if (alreadyDeleted)
            {
                secrets.SetupSequence(c => c.StartDeleteSecretAsync("IntegrationTest-secret", It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new RequestFailedException(409, "Another framework is deleting this secret."))
                    .ThrowsAsync(new RequestFailedException(404, "The secret has been deleted."));
            }
            else
            {
                secrets.SetupSequence(c => c.StartDeleteSecretAsync("IntegrationTest-secret", It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new RequestFailedException(409, "Another framework is deleting this secret."))
                    .ReturnsAsync(operation.Object);
            }

            await resources.SetSecretAsync("IntegrationTest-secret", "value");
            await resources.CleanupAsync(output.Add);
            await resources.CleanupAsync(output.Add);

            secrets.Verify(c => c.StartDeleteSecretAsync("IntegrationTest-secret", It.IsAny<CancellationToken>()), Times.Exactly(2));
            operation.Verify(o => o.WaitForCompletionAsync(It.IsAny<CancellationToken>()),
                alreadyDeleted ? Times.Never() : Times.Once());
            Assert.Contains(output, message => message.StartsWith("Secret deletion conflict", StringComparison.Ordinal));
            Assert.Contains("0 resources remaining", output.Last());
        }

        [Fact]
        public async Task CleanupReportsPersistentSecretDeletionConflictsAfterBoundedRetries()
        {
            Mock<ConfigurationClient> client = CreateConfigurationClient();
            Mock<SecretClient> secrets = CreateSecretClient();
            var resources = new IntegrationTestResources(client.Object, secrets.Object);
            var output = new List<string>();
            secrets.Setup(c => c.StartDeleteSecretAsync("IntegrationTest-conflict", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new RequestFailedException(409, "Persistent conflict."));
            await resources.SetSecretAsync("IntegrationTest-conflict", "value");
            await resources.SetSecretAsync("IntegrationTest-other", "value");

            AggregateException exception = await Assert.ThrowsAsync<AggregateException>(() => resources.CleanupAsync(output.Add));

            Assert.Equal(409, Assert.IsType<RequestFailedException>(Assert.Single(exception.InnerExceptions)).Status);
            secrets.Verify(c => c.StartDeleteSecretAsync("IntegrationTest-conflict", It.IsAny<CancellationToken>()), Times.Exactly(4));
            secrets.Verify(c => c.StartDeleteSecretAsync("IntegrationTest-other", It.IsAny<CancellationToken>()), Times.Once());
            Assert.Equal(3, output.Count(message => message.StartsWith("Secret deletion conflict", StringComparison.Ordinal)));
            Assert.Contains("1 resources remaining", output.Last());
        }

        [Fact]
        public async Task CleanupCanCancelSecretDeletionConflictBackoff()
        {
            Mock<ConfigurationClient> client = CreateConfigurationClient();
            Mock<SecretClient> secrets = CreateSecretClient();
            var resources = new IntegrationTestResources(client.Object, secrets.Object);
            var output = new List<string>();
            using var cancellation = new CancellationTokenSource();
            secrets.Setup(c => c.StartDeleteSecretAsync("IntegrationTest-secret", cancellation.Token))
                .Callback(() => cancellation.Cancel())
                .ThrowsAsync(new RequestFailedException(409, "Conflict."));
            await resources.SetSecretAsync("IntegrationTest-secret", "value");

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resources.CleanupAsync(output.Add, cancellation.Token));

            secrets.Verify(c => c.StartDeleteSecretAsync("IntegrationTest-secret", It.IsAny<CancellationToken>()), Times.Once());
        }

        [Fact]
        public async Task CleanupCanRetryFailedResourcesWithoutRepeatingSuccessfulCleanup()
        {
            Mock<ConfigurationClient> client = CreateConfigurationClient();
            Mock<SecretClient> secrets = CreateSecretClient();
            var resources = new IntegrationTestResources(client.Object, secrets.Object);
            var output = new List<string>();

            await resources.SetConfigurationSettingAsync(new ConfigurationSetting("IntegrationTest-failed", "value"));
            await resources.SetConfigurationSettingAsync(new ConfigurationSetting("IntegrationTest-other", "value"));
            client.SetupSequence(c => c.DeleteConfigurationSettingAsync("IntegrationTest-failed", null, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new RequestFailedException(503, "Retry later."))
                .ReturnsAsync(Mock.Of<Response>());

            await Assert.ThrowsAsync<AggregateException>(() => resources.CleanupAsync(output.Add));
            await resources.CleanupAsync(output.Add);

            client.Verify(c => c.DeleteConfigurationSettingAsync("IntegrationTest-failed", null, It.IsAny<CancellationToken>()), Times.Exactly(2));
            client.Verify(c => c.DeleteConfigurationSettingAsync("IntegrationTest-other", null, It.IsAny<CancellationToken>()), Times.Once());
            Assert.Contains("0 resources remaining", output.Last());
        }

        [Fact]
        public async Task CleanupWaitsForSecretDeletionToComplete()
        {
            Mock<ConfigurationClient> client = CreateConfigurationClient();
            Mock<SecretClient> secrets = CreateSecretClient();
            var resources = new IntegrationTestResources(client.Object, secrets.Object);
            var output = new List<string>();
            var completion = new TaskCompletionSource<Response<DeletedSecret>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var operation = new Mock<DeleteSecretOperation>(MockBehavior.Strict);
            operation.Setup(o => o.WaitForCompletionAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<Response<DeletedSecret>>(completion.Task));
            secrets.Setup(c => c.StartDeleteSecretAsync("IntegrationTest-secret", It.IsAny<CancellationToken>()))
                .ReturnsAsync(operation.Object);
            await resources.SetSecretAsync("IntegrationTest-secret", "value");

            Task cleanup = resources.CleanupAsync(output.Add);
            Assert.False(cleanup.IsCompleted);
            completion.SetResult(Response.FromValue(
                SecretModelFactory.DeletedSecret(new SecretProperties("IntegrationTest-secret")), Mock.Of<Response>()));
            await cleanup;

            operation.Verify(o => o.WaitForCompletionAsync(It.IsAny<CancellationToken>()), Times.Once());
        }

        [Fact]
        public async Task CleanupDoesNotTouchAnotherTestsResources()
        {
            Mock<ConfigurationClient> client = CreateConfigurationClient();
            Mock<SecretClient> secrets = CreateSecretClient();
            var first = new IntegrationTestResources(client.Object, secrets.Object);
            var second = new IntegrationTestResources(client.Object, secrets.Object);
            var output = new List<string>();
            await first.SetConfigurationSettingAsync(new ConfigurationSetting("IntegrationTest-first", "value"));
            await second.SetConfigurationSettingAsync(new ConfigurationSetting("IntegrationTest-second", "value"));

            await first.CleanupAsync(output.Add);

            client.Verify(c => c.DeleteConfigurationSettingAsync("IntegrationTest-first", null, It.IsAny<CancellationToken>()), Times.Once());
            client.Verify(c => c.DeleteConfigurationSettingAsync("IntegrationTest-second", null, It.IsAny<CancellationToken>()), Times.Never());
            await second.CleanupAsync(output.Add);
            client.Verify(c => c.DeleteConfigurationSettingAsync("IntegrationTest-second", null, It.IsAny<CancellationToken>()), Times.Once());
        }

        [Fact]
        public async Task StaleCleanupPreservesFreshAndUnrelatedResourcesAndFindsLegacySnapshots()
        {
            Mock<ConfigurationClient> client = CreateConfigurationClient();
            Mock<SecretClient> secrets = CreateSecretClient();
            var resources = new IntegrationTestResources(client.Object, secrets.Object);
            var output = new List<string>();
            DateTimeOffset cutoff = DateTimeOffset.UtcNow.Subtract(TimeSpan.FromHours(3));
            DateTimeOffset old = cutoff.AddSeconds(-1);
            string flag = FeatureManagementConstants.FeatureFlagMarker + "IntegrationTest-old";
            string[] staleSnapshotNames =
            {
                "snapshot-IntegrationTest-canonical",
                "snapshot-key-IntegrationTest-legacy",
                "snapshot-invalid-IntegrationTest-legacy",
                "snapshot-ff-IntegrationTest-legacy",
                "IntegrationTest-legacy-snapshot"
            };
            var snapshots = staleSnapshotNames.Select(name => ConfigurationModelFactory.ConfigurationSnapshot(
                name: name, status: ConfigurationSnapshotStatus.Ready, createdOn: old)).ToList();
            snapshots.Add(ConfigurationModelFactory.ConfigurationSnapshot(
                name: "snapshot-IntegrationTest-fresh", status: ConfigurationSnapshotStatus.Ready, createdOn: cutoff));
            snapshots.Add(ConfigurationModelFactory.ConfigurationSnapshot(
                name: "snapshot-IntegrationTest-archived", status: ConfigurationSnapshotStatus.Archived, createdOn: old));
            snapshots.Add(ConfigurationModelFactory.ConfigurationSnapshot(
                name: "snapshot-production", status: ConfigurationSnapshotStatus.Ready, createdOn: old));
            snapshots.Add(ConfigurationModelFactory.ConfigurationSnapshot(
                name: "IntegrationTest-not-a-snapshot-name", status: ConfigurationSnapshotStatus.Ready, createdOn: old));
            snapshots.Add(ConfigurationModelFactory.ConfigurationSnapshot(
                name: "snapshot-IntegrationTestOther", status: ConfigurationSnapshotStatus.Ready, createdOn: old));
            snapshots.Add(ConfigurationModelFactory.ConfigurationSnapshot(
                name: "snapshot-IntegrationTest-unknown-age", status: ConfigurationSnapshotStatus.Ready));

            SetupStaleListings(client, secrets,
                new[]
                {
                    ConfigurationModelFactory.ConfigurationSetting("IntegrationTest-old", "value", label: "label", lastModified: old),
                    ConfigurationModelFactory.ConfigurationSetting(flag, "{}", lastModified: old),
                    ConfigurationModelFactory.ConfigurationSetting("IntegrationTest-fresh", "value", lastModified: cutoff),
                    ConfigurationModelFactory.ConfigurationSetting("IntegrationTest-unknown-age", "value"),
                    ConfigurationModelFactory.ConfigurationSetting("Production-setting", "value", lastModified: old)
                },
                snapshots,
                new[]
                {
                    SecretModelFactory.SecretProperties(name: "IntegrationTest-old", createdOn: old),
                    SecretModelFactory.SecretProperties(name: "IntegrationTest-fresh", createdOn: cutoff),
                    SecretModelFactory.SecretProperties(name: "IntegrationTest-unknown-age"),
                    SecretModelFactory.SecretProperties(name: "Production-secret", createdOn: old),
                    SecretModelFactory.SecretProperties(name: "IntegrationTestOther", createdOn: old)
                });

            await resources.CleanupStaleResourcesAsync(cutoff, output.Add);

            client.Verify(c => c.DeleteConfigurationSettingAsync("IntegrationTest-old", "label", It.IsAny<CancellationToken>()), Times.Once());
            client.Verify(c => c.DeleteConfigurationSettingAsync(flag, null, It.IsAny<CancellationToken>()), Times.Once());
            client.Verify(c => c.DeleteConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
            foreach (string name in staleSnapshotNames)
            {
                client.Verify(c => c.ArchiveSnapshotAsync(name, It.IsAny<CancellationToken>()), Times.Once());
            }

            client.Verify(c => c.ArchiveSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(staleSnapshotNames.Length));
            client.Verify(c => c.GetSnapshotsAsync(
                It.Is<SnapshotSelector>(selector => selector.Status.Contains(ConfigurationSnapshotStatus.Ready)), It.IsAny<CancellationToken>()), Times.Once());
            secrets.Verify(c => c.StartDeleteSecretAsync("IntegrationTest-old", It.IsAny<CancellationToken>()), Times.Once());
            secrets.Verify(c => c.StartDeleteSecretAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once());
        }

        [Theory]
        [InlineData(404)]
        [InlineData(403)]
        public async Task StaleCleanupContinuesAfterListingFailures(int status)
        {
            Mock<ConfigurationClient> client = CreateConfigurationClient();
            Mock<SecretClient> secrets = CreateSecretClient();
            var resources = new IntegrationTestResources(client.Object, secrets.Object);
            var output = new List<string>();
            DateTimeOffset cutoff = DateTimeOffset.UtcNow.Subtract(TimeSpan.FromHours(3));
            DateTimeOffset old = cutoff.AddSeconds(-1);
            string flag = FeatureManagementConstants.FeatureFlagMarker + "IntegrationTest-old";
            SetupStaleListings(client, secrets,
                new[] { ConfigurationModelFactory.ConfigurationSetting(flag, "{}", lastModified: old) },
                new[] { ConfigurationModelFactory.ConfigurationSnapshot(
                    name: "snapshot-IntegrationTest-old", status: ConfigurationSnapshotStatus.Ready, createdOn: old) },
                new[] { SecretModelFactory.SecretProperties(name: "IntegrationTest-old", createdOn: old) });
            client.Setup(c => c.GetConfigurationSettingsAsync(
                It.Is<SettingSelector>(selector => selector.KeyFilter == "IntegrationTest-*"), It.IsAny<CancellationToken>()))
                .Throws(new RequestFailedException(status, "Listing failed."));

            AggregateException exception = await Assert.ThrowsAsync<AggregateException>(() => resources.CleanupStaleResourcesAsync(cutoff, output.Add));

            Assert.Single(exception.InnerExceptions);
            client.Verify(c => c.DeleteConfigurationSettingAsync(flag, null, It.IsAny<CancellationToken>()), Times.Once());
            client.Verify(c => c.ArchiveSnapshotAsync("snapshot-IntegrationTest-old", It.IsAny<CancellationToken>()), Times.Once());
            secrets.Verify(c => c.StartDeleteSecretAsync("IntegrationTest-old", It.IsAny<CancellationToken>()), Times.Once());
        }

        private static Mock<ConfigurationClient> CreateConfigurationClient()
        {
            var client = new Mock<ConfigurationClient>(MockBehavior.Strict);
            client.Setup(c => c.SetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), false, It.IsAny<CancellationToken>()))
                .ReturnsAsync((ConfigurationSetting setting, bool onlyIfUnchanged, CancellationToken token) =>
                    Response.FromValue(setting, Mock.Of<Response>()));
            client.Setup(c => c.DeleteConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Mock.Of<Response>());
            client.Setup(c => c.CreateSnapshotAsync(WaitUntil.Completed, It.IsAny<string>(), It.IsAny<ConfigurationSnapshot>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Mock.Of<CreateSnapshotOperation>());
            client.Setup(c => c.ArchiveSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(CreateSnapshot(), Mock.Of<Response>()));
            return client;
        }

        private static Mock<SecretClient> CreateSecretClient()
        {
            var client = new Mock<SecretClient>(MockBehavior.Strict);
            client.Setup(c => c.SetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string name, string value, CancellationToken token) =>
                    Response.FromValue(new KeyVaultSecret(name, value), Mock.Of<Response>()));
            var operation = new Mock<DeleteSecretOperation>(MockBehavior.Strict);
            operation.Setup(o => o.WaitForCompletionAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<Response<DeletedSecret>>(Response.FromValue(
                    SecretModelFactory.DeletedSecret(new SecretProperties("IntegrationTest-secret")), Mock.Of<Response>())));
            client.Setup(c => c.StartDeleteSecretAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(operation.Object);
            return client;
        }

        private static ConfigurationSnapshot CreateSnapshot()
        {
            return new ConfigurationSnapshot(new[] { new ConfigurationSettingsFilter("IntegrationTest-*") });
        }

        private static void SetupStaleListings(
            Mock<ConfigurationClient> client,
            Mock<SecretClient> secrets,
            IEnumerable<ConfigurationSetting> settings,
            IEnumerable<ConfigurationSnapshot> snapshots,
            IEnumerable<SecretProperties> secretProperties)
        {
            client.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns((SettingSelector selector, CancellationToken token) =>
                    CreatePageable(settings.Where(setting => setting.Key.StartsWith(selector.KeyFilter.TrimEnd('*'), StringComparison.Ordinal))));
            client.Setup(c => c.GetSnapshotsAsync(It.IsAny<SnapshotSelector>(), It.IsAny<CancellationToken>()))
                .Returns(CreatePageable(snapshots));
            secrets.Setup(c => c.GetPropertiesOfSecretsAsync(It.IsAny<CancellationToken>()))
                .Returns(CreatePageable(secretProperties));
        }

        private static AsyncPageable<T> CreatePageable<T>(IEnumerable<T> values) where T : notnull
        {
            return AsyncPageable<T>.FromPages(new[] { Page<T>.FromValues(values.ToList(), null, Mock.Of<Response>()) });
        }
    }
}
