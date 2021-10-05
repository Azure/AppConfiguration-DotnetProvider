
using System.Text.Json;
using System.Text;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
	/// <summary>
	/// Summary for PushNotificationParser
	/// </summary>
	public static class PushNotificationParser
	{
		/// <summary>
		///  Tries to parse the push notification
		/// </summary>
		/// <param name="message"> Message Data returned from the provider</param>
		/// <param name="pushNotification"> out parameter which will try to be populated</param>
		/// <returns></returns>
		public static bool TryParsePushNotification(string message, out PushNotification pushNotification)
		{
			PushNotification testing = new PushNotification();

			if (message == null)
			{
				pushNotification = testing;
				return false;
			}

			try
			{

				JsonElement jsonMessage = JsonDocument.Parse(message).RootElement;

				testing.SyncToken = jsonMessage.GetProperty("data").GetProperty("syncToken").GetString();
				testing.EventType = jsonMessage.GetProperty("eventType").GetString();
				testing.Uri = jsonMessage.GetProperty("topic").GetString(); ;

				pushNotification = testing;
			}
			catch
			{
				pushNotification = testing;
				return false;
			}


			return true;
		}
	}
}