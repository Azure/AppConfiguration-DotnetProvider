using Azure.Data.AppConfiguration;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class SelectorFactory
    {
        public static SettingSelector CreateSettingSelector()
        {
            return new SettingSelector();
        }

        public static SettingSelector CreateSettingSelector(string keyFilter, string labelFilter, SettingFields fields = SettingFields.All)
        {
            var selector = new SettingSelector
            {
                KeyFilter = keyFilter,
                LabelFilter = labelFilter,
                Fields = fields
            };

            return selector;
        }
    }
}
