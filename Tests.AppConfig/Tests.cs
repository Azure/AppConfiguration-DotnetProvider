namespace Tests.AppConfig
{
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Configuration.AppConfig;
    using Microsoft.Extensions.Primitives;
    using System;
    using System.Text;
    using System.Threading.Tasks;
    using Xunit;

    public class Tests
    {
        [Fact]
        public void AddsConfigurationValues()
        {
            var testClient = new AppConfigClient();

            testClient.Data["TestKey"] = new KeyValue()
            {
                Key = "TestKey",
                Value = "TestValue",
            };

            var builder = new ConfigurationBuilder();

            builder.AddRemoteAppConfiguration("FakeUrl", new RemoteConfigurationOptions(), testClient);

            var config = builder.Build();

            Assert.True(config["TestKey"] == "TestValue");
        }

        [Fact]
        public void TriggersChangeNotification()
        {
            var testClient = new AppConfigClient();

            testClient.Data["TestKey"] = new KeyValue()
            {
                Key = "TestKey",
                Value = "TestValue",
            };

            var builder = new ConfigurationBuilder();

            builder.AddRemoteAppConfiguration("FakeUrl", new RemoteConfigurationOptions().Listen("TestKey", 1000), testClient);

            var config = builder.Build();

            bool changeNotified = false;

            var tcs = new TaskCompletionSource<object>();

            ChangeToken.OnChange(() => config.GetReloadToken(), () =>
            {
                changeNotified = true;
                tcs.SetResult(null);
            });

            testClient.Data["TestKey"].Value = "NewValue";

            tcs.Task.Wait(5000);

            Assert.True(changeNotified);
        }

        [Fact]
        public void Integrates()
        {
            string configUri = "";

            //
            // Integration test currently requires active server

            if (string.IsNullOrEmpty(configUri))
            {
                return;
            }

            var builder = new ConfigurationBuilder();

            builder.AddRemoteAppConfiguration(configUri);

            var config = builder.Build();

            Assert.True(config["connectionString"] == "3.1");
        }

        [Fact]
        public void FormatsKeyValue()
        {
            var testClient = new AppConfigClient();

            testClient.Data["TestKey"] = new KeyValue()
            {
                Key = "TestKey",
                Value = Convert.ToBase64String(Encoding.Unicode.GetBytes("TestValue")),
                ContentType = "text/base64"
            };

            var builder = new ConfigurationBuilder();

            builder.AddRemoteAppConfiguration("FakeUrl", new RemoteConfigurationOptions() {
                KeyValueFormatter = new KeyValueFormatter()
            }, testClient);

            var config = builder.Build();

            Assert.True(config["TestKey"] == "TestValue");
        }
    }
}
