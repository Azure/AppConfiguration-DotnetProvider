// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions
{
    internal static class TaskExtensions
    {
        public static async Task ObserveCancellation(this Task task, Logger logger)
        {
			try
			{
				await task.ConfigureAwait(false);
			}
			catch (OperationCanceledException ex)
			{
				logger.LogWarning(LogHelper.BuildFallbackClientLookupFailMessage(ex.Message));
			}
        }
    }
}
