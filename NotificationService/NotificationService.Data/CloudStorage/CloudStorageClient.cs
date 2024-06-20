﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace NotificationService.Data
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Azure.Identity;
    using Azure.Storage.Blobs;
    using Azure.Storage.Queues;
    using Microsoft.Extensions.Options;
    using NotificationService.Common;
    using NotificationService.Common.Logger;

    /// <summary>
    /// Client Interface to the Azure Cloud Storage.
    /// </summary>
    public class CloudStorageClient : ICloudStorageClient
    {
        /// <summary>
        /// Instance of <see cref="StorageAccountSetting"/>.
        /// </summary>
        private readonly StorageAccountSetting storageAccountSetting;

        /// <summary>
        /// Instance of <see cref="QueueClient"/>.
        /// </summary>
        private readonly QueueClient cloudQueueClient;

        /// <summary>
        /// Instance of <see cref="BlobContainerClient"/>.
        /// </summary>
        private readonly BlobContainerClient blobContainerClient;

        /// <summary>
        /// Instance of <see cref="ILogger"/>.
        /// </summary>
        private readonly ILogger logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="CloudStorageClient"/> class.
        /// </summary>
        /// <param name="storageAccountSetting">Storage Account configuration.</param>
        /// <param name="logger"><see cref="ILogger"/> instance.</param>
        public CloudStorageClient(IOptions<StorageAccountSetting> storageAccountSetting, ILogger logger)
        {
            this.storageAccountSetting = storageAccountSetting?.Value;
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            //this.cloudStorageAccount = CloudStorageAccount.Parse(this.storageAccountSetting.ConnectionString);
            //this.cloudQueueClient = this.cloudStorageAccount.CreateCloudQueueClient();

            this.cloudQueueClient = new QueueClient(new Uri(this.storageAccountSetting.QueueConnectionName), new DefaultAzureCredential());

            this.blobContainerClient = new BlobContainerClient(new Uri(this.storageAccountSetting.BlobConnectionName), new DefaultAzureCredential());
            if (!this.blobContainerClient.Exists())
            {
                this.logger.TraceWarning($"BlobStorageClient - Method: {nameof(QueueClient)} - No container found with name {this.storageAccountSetting.BlobContainerName}.");

                var response = this.blobContainerClient.CreateIfNotExists();

                this.blobContainerClient = new BlobContainerClient(new Uri(this.storageAccountSetting.BlobContainerName), new DefaultAzureCredential());
            }
        }

        /// <inheritdoc/>
        public QueueClient GetCloudQueue(string queueName)
        {
            //CloudQueue cloudQueue = this.cloudQueueClient.GetQueueReference(queueName);
            //_ = cloudQueue.CreateIfNotExists();
            //return cloudQueue;

            _ = this.cloudQueueClient.CreateIfNotExists();
            return this.cloudQueueClient;
        }

        /// <inheritdoc/>
        public Task QueueCloudMessages(IEnumerable<string> messages, TimeSpan? initialVisibilityDelay = null)
        {
            messages.ToList().ForEach(msg =>
            {
                //CloudQueueMessage message = new CloudQueueMessage(msg);
                //cloudQueue.AddMessage(message, null, initialVisibilityDelay);

                _ = this.cloudQueueClient.SendMessage(msg);
            });
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public async Task<string> UploadBlobAsync(string blobName, string content)
        {
            BlobClient blobClient = this.blobContainerClient.GetBlobClient(blobName);
            var contentBytes = Convert.FromBase64String(content);
            using (var stream = new MemoryStream(contentBytes))
            {
                var result = await blobClient.UploadAsync(stream, overwrite: true).ConfigureAwait(false);
            }

            return string.Concat(this.blobContainerClient.Uri, "/", blobName);
        }

        /// <inheritdoc/>
        public async Task<string> DownloadBlobAsync(string blobName)
        {
            BlobClient blobClient = this.blobContainerClient.GetBlobClient(blobName);
            bool isExists = await blobClient.ExistsAsync().ConfigureAwait(false);
            if (isExists)
            {
                var blob = await blobClient.DownloadAsync().ConfigureAwait(false);
                byte[] streamArray = new byte[blob.Value.ContentLength];
                long numBytesToRead = blob.Value.ContentLength;
                int numBytesRead = 0;
                int maxBytesToRead = 10;
                do
                {
                    if (numBytesToRead < maxBytesToRead)
                    {
                        maxBytesToRead = (int)numBytesToRead;
                    }

                    int n = blob.Value.Content.Read(streamArray, numBytesRead, maxBytesToRead);
                    numBytesRead += n;
                    numBytesToRead -= n;
                }
                while (numBytesToRead > 0);

                return Convert.ToBase64String(streamArray);
            }
            else
            {
                this.logger.TraceWarning($"BlobStorageClient - Method: {nameof(this.DownloadBlobAsync)} - No blob found with name {blobName}.");
                return null;
            }
        }

        /// <inheritdoc/>
        public async Task<bool> DeleteBlobsAsync(string blobName)
        {
            BlobClient blobClient = this.blobContainerClient.GetBlobClient(blobName);
            bool isExists = await blobClient.ExistsAsync().ConfigureAwait(false);
            if (isExists)
            {
                var response = await blobClient.DeleteAsync().ConfigureAwait(false);
                return true;
            }
            else
            {
                this.logger.TraceWarning($"BlobStorageClient - Method: {nameof(this.DeleteBlobsAsync)} - No blob found with name {blobName}.");
                return false;
            }
        }
    }
}
