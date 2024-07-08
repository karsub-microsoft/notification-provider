// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace NotificationService.Data
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Azure.Data.Tables;
    using Microsoft.Extensions.Options;
    using NotificationService.Common;
    using NotificationService.Common.Logger;
    using NotificationService.Contracts.Entities;

    /// <summary>
    /// Repository for mail templates.
    /// </summary>
    public class MailTemplateRepository : IMailTemplateRepository
    {
        private readonly ILogger logger;
        private readonly ICloudStorageClient cloudStorageClient;
        private readonly ITableStorageClient tableStorageClient;
        private readonly TableClient cloudTable;

        /// <summary>
        /// Initializes a new instance of the <see cref="MailTemplateRepository"/> class.
        /// </summary>
        /// <param name="logger">logger.</param>
        /// <param name="cloudStorageClient">cloud storage client for blob storage.</param>
        /// <param name="tableStorageClient">cloud storage client for table storage.</param>
        /// <param name="storageAccountSetting">primary key of storage account.</param>
        public MailTemplateRepository(
            ILogger logger,
            ICloudStorageClient cloudStorageClient,
            ITableStorageClient tableStorageClient,
            IOptions<StorageAccountSetting> storageAccountSetting)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.cloudStorageClient = cloudStorageClient ?? throw new ArgumentNullException(nameof(cloudStorageClient));
            this.tableStorageClient = tableStorageClient ?? throw new ArgumentNullException(nameof(tableStorageClient));

            if (storageAccountSetting is null)
            {
                throw new ArgumentNullException(nameof(storageAccountSetting));
            }

            if (string.IsNullOrWhiteSpace(storageAccountSetting?.Value?.MailTemplateTableName))
            {
                this.logger.WriteException(new ArgumentException("MailTemplateTableName"));
                throw new ArgumentException("MailTemplateTableName");
            }

            this.cloudTable = this.tableStorageClient.GetCloudTable(storageAccountSetting.Value.MailTemplateTableName);
        }

        /// <inheritdoc/>
        public async Task<MailTemplateEntity> GetMailTemplate(string applicationName, string templateName)
        {
            var traceProps = new Dictionary<string, string>();
            traceProps[AIConstants.Application] = applicationName;
            traceProps[AIConstants.MailTemplateName] = templateName;

            this.logger.TraceInformation($"Started {nameof(this.GetMailTemplate)} method of {nameof(MailTemplateRepository)}.", traceProps);

            string blobName = GetBlobName(applicationName, templateName);
            var contentTask = this.cloudStorageClient.DownloadBlobAsync(blobName).ConfigureAwait(false);

            var retrievedResult = await this.cloudTable.GetEntityAsync<MailTemplateEntity>(applicationName, templateName).ConfigureAwait(false);

            this.logger.TraceInformation($"Finished {nameof(this.GetMailTemplate)} method of {nameof(MailTemplateRepository)}.", traceProps);

            return retrievedResult;
        }

        /// <inheritdoc/>
        public async Task<IList<MailTemplateEntity>> GetAllTemplateEntities(string applicationName)
        {
            var traceProps = new Dictionary<string, string>();
            traceProps[AIConstants.Application] = applicationName;

            this.logger.TraceInformation($"Started {nameof(this.GetAllTemplateEntities)} method of {nameof(MailTemplateRepository)}.", traceProps);

            List<MailTemplateEntity> mailTemplateEntities = new List<MailTemplateEntity>();
            //var filterPartitionkey = TableQuery.GenerateFilterCondition(nameof(MailTemplateEntity.PartitionKey), QueryComparisons.Equal, applicationName);
            //var query = new TableQuery<MailTemplateEntity>().Where(filterPartitionkey);
            //TableContinuationToken continuationToken = null;
            //do
            //{
            var pages = this.cloudTable.QueryAsync<MailTemplateEntity>(a => a.PartitionKey.Equals(applicationName, StringComparison.InvariantCultureIgnoreCase)).AsPages().ConfigureAwait(false);
            await foreach (var page in pages)
            {
                mailTemplateEntities.AddRange(page.Values);
            }

            //}
            //while (continuationToken != null);

            this.logger.TraceInformation($"Finished {nameof(this.GetAllTemplateEntities)} method of {nameof(MailTemplateRepository)}.", traceProps);

            return mailTemplateEntities;
        }

        /// <inheritdoc/>
        public async Task<bool> UpsertEmailTemplateEntities(MailTemplateEntity mailTemplateEntity)
        {
            bool result = false;
            this.logger.TraceInformation($"Started {nameof(this.UpsertEmailTemplateEntities)} method of {nameof(MailTemplateRepository)}.");

            if (mailTemplateEntity is null)
            {
                throw new ArgumentNullException(nameof(mailTemplateEntity));
            }

            string blobName = GetBlobName(mailTemplateEntity.Application, mailTemplateEntity.TemplateId);
            string blobUri = await this.cloudStorageClient.UploadBlobAsync(
                blobName,
                mailTemplateEntity.Content)
                .ConfigureAwait(false);

            // Making sure content is not stored in table storage
            mailTemplateEntity.Content = null;

            // Create the TableOperation object that inserts the entity.
            var response = await this.cloudTable.UpsertEntityAsync(mailTemplateEntity).ConfigureAwait(false);

            // .ExecuteAsync(insertOperation).ConfigureAwait(false);
            if (!response.IsError)
            {
                result = true;
            }

            this.logger.TraceInformation($"Finished {nameof(this.UpsertEmailTemplateEntities)} method of {nameof(MailTemplateRepository)}.");

            return result;
        }

        /// <inheritdoc/>
        public async Task<bool> DeleteMailTemplate(string applicationName, string templateName)
        {
            var traceProps = new Dictionary<string, string>();
            traceProps[AIConstants.Application] = applicationName;
            traceProps[AIConstants.MailTemplateName] = templateName;

            this.logger.TraceInformation($"Started {nameof(this.DeleteMailTemplate)} method of {nameof(MailTemplateRepository)}.", traceProps);
            bool result = false;
            string blobName = GetBlobName(applicationName, templateName);
            var status = await this.cloudStorageClient.DeleteBlobsAsync(blobName).ConfigureAwait(false);

            if (status)
            {
                // Retrieve the entity to be deleted.
                //    TableOperation retrieveOperation = TableOperation.Retrieve<MailTemplateEntity>(applicationName, templateName);
                //    TableResult tableResult = await this.cloudTable.GetEntityAsync<MailTemplateEntity>(applicationName, templateName).ConfigureAwait(false);
                //    // ExecuteAsync(retrieveOperation).ConfigureAwait(false);
                //    MailTemplateEntity mailTemplateEntity = tableResult.Result as MailTemplateEntity;

                //// Create the TableOperation object that Delets the entity.
                // TableOperation deleteOperation = TableOperation.Delete(mailTemplateEntity);
                var response = await this.cloudTable.DeleteEntityAsync(applicationName, templateName).ConfigureAwait(false);
                result = true;
            }

            this.logger.TraceInformation($"Finished {nameof(this.DeleteMailTemplate)} method of {nameof(MailTemplateRepository)}.", traceProps);

            return result;
        }

        /// <summary>
        /// Gets blob name.
        /// </summary>
        /// <param name="applicationName">Application sourcing the email template.</param>
        /// <param name="templateName">Mail template name.</param>
        /// <returns>Blob name.</returns>
        private static string GetBlobName(string applicationName, string templateName)
        {
            return $"{applicationName}/EmailTemplates/{templateName}";
        }
    }
}
