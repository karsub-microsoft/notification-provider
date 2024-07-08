// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace NotificationService.Data
{
    using Azure.Data.Tables;
    using Azure.Identity;
    using Microsoft.Extensions.Options;
    using NotificationService.Common;
    using System;

    /// <summary>
    /// Client Interface to the Azure Cloud Storage.
    /// </summary>
    public class TableStorageClient : ITableStorageClient
    {
        /// <summary>
        /// Instance of <see cref="StorageAccountSetting"/>.
        /// </summary>
        private readonly StorageAccountSetting storageAccountSetting;

        /// <summary>
        /// Instance of <see cref="CloudTableClient"/>.
        /// </summary>
        private readonly TableServiceClient cloudTableServiceClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="TableStorageClient"/> class.
        /// </summary>
        /// <param name="storageAccountSetting">Storage Account configuration.</param>
        public TableStorageClient(IOptions<StorageAccountSetting> storageAccountSetting)
        {
            this.storageAccountSetting = storageAccountSetting?.Value;
            this.cloudTableServiceClient = new TableServiceClient(new Uri(this.storageAccountSetting.TableConnectionName), new DefaultAzureCredential());
        }

        /// <summary>
        /// Table client for the specific table.
        /// </summary>
        /// <param name="tableName">Table name</param>
        /// <returns>Table Client Instance</returns>
        public TableClient GetCloudTable(string tableName)
        {
            TableClient cloudTable = this.cloudTableServiceClient.GetTableClient(tableName);

            // _ = cloudTable.CreateIfNotExists();
            return cloudTable;
        }

        /*  /// <inheritdoc/>
          public Task QueueCloudMessages(CloudQueue cloudQueue, IEnumerable<string> messages, TimeSpan? initialVisibilityDelay = null)
          {
              messages.ToList().ForEach(msg =>
              {
                  CloudQueueMessage message = new CloudQueueMessage(msg);
                  cloudQueue.AddMessage(message, null, initialVisibilityDelay);
              });
              return Task.CompletedTask;
          }*/
    }
}
