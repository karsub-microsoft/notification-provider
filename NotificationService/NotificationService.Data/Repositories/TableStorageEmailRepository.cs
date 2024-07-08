// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace NotificationService.Data.Repositories
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Threading.Tasks;
    using Azure;
    using Azure.Data.Tables;
    using Microsoft.Azure.Cosmos;
    using Microsoft.Extensions.Options;
    using Newtonsoft.Json;
    using NotificationService.Common;
    using NotificationService.Common.Logger;
    using NotificationService.Common.Utility;
    using NotificationService.Contracts;
    using NotificationService.Contracts.Entities;
    using NotificationService.Contracts.Extensions;
    using NotificationService.Contracts.Models.Request;
    using NotificationService.Data.Utilities;

    /// <summary>
    /// Repository for TableStorage.
    /// </summary>
    public class TableStorageEmailRepository : IEmailNotificationRepository
    {
        /// <summary>
        /// Instance of StorageAccountSetting Configuration.
        /// </summary>
        private readonly StorageAccountSetting storageAccountSetting;

        /// <summary>
        /// Instance of Application Configuration.
        /// </summary>
        private readonly ITableStorageClient cloudStorageClient;

        /// <summary>
        /// Instance of <see cref="emailHistoryTable"/>.
        /// </summary>
        private readonly TableClient emailHistoryTable;

        /// <summary>
        /// Instance of <see cref="meetingHistoryTable"/>.
        /// </summary>
        private readonly TableClient meetingHistoryTable;

        /// <summary>
        /// Instance of Cosmos DB Configuration.
        /// </summary>
        private readonly CosmosDBSetting cosmosDBSetting;

        /// <summary>
        /// Instance of Application Configuration.
        /// </summary>
        private readonly ICosmosDBQueryClient cosmosDBQueryClient;

        /// <summary>
        /// Instance of <see cref="Container"/>.
        /// </summary>
        private readonly Container eventsContainer;

        /// <summary>
        /// Instance of <see cref="ICosmosLinqQuery"/>.
        /// </summary>
        private readonly ICosmosLinqQuery cosmosLinqQuery;

        /// <summary>
        /// Instance of <see cref="ILogger"/>.
        /// </summary>
        private readonly ILogger logger;

        /// <summary>
        /// Instance of <see cref="IMailAttachmentRepository"/>.
        /// </summary>
        private readonly IMailAttachmentRepository mailAttachmentRepository;

        /// <summary>
        /// Initializes a new instance of the <see cref="TableStorageEmailRepository"/> class.
        /// </summary>
        /// <param name="cosmosDBSetting">Cosmos DB Configuration.</param>
        /// <param name="cosmosDBQueryClient">CosmosDB Query Client.</param>
        /// <param name="cosmosLinqQuery">Instance of Cosmos Linq query.</param>
        /// <param name="storageAccountSetting">primary key of storage account.</param>
        /// <param name="cloudStorageClient"> cloud storage client for table storage.</param>
        /// <param name="logger">logger.</param>
        /// <param name="mailAttachmentRepository">Instnce of the Mail Attachment Repository.</param>
        public TableStorageEmailRepository(IOptions<CosmosDBSetting> cosmosDBSetting, ICosmosDBQueryClient cosmosDBQueryClient, ICosmosLinqQuery cosmosLinqQuery, IOptions<StorageAccountSetting> storageAccountSetting, ITableStorageClient cloudStorageClient, ILogger logger, IMailAttachmentRepository mailAttachmentRepository)
        {
            this.cosmosDBSetting = cosmosDBSetting?.Value ?? throw new System.ArgumentNullException(nameof(cosmosDBSetting));
            this.cosmosDBQueryClient = cosmosDBQueryClient ?? throw new System.ArgumentNullException(nameof(cosmosDBQueryClient));
            this.eventsContainer = this.cosmosDBQueryClient.GetCosmosContainer(this.cosmosDBSetting.Database, this.cosmosDBSetting.EventsContainer);
            this.storageAccountSetting = storageAccountSetting?.Value ?? throw new System.ArgumentNullException(nameof(storageAccountSetting));
            this.cloudStorageClient = cloudStorageClient ?? throw new System.ArgumentNullException(nameof(cloudStorageClient));
            var emailHistoryTableName = storageAccountSetting?.Value?.EmailHistoryTableName;
            var meetingHistoryTableName = storageAccountSetting?.Value?.MeetingHistoryTableName;
            if (string.IsNullOrEmpty(emailHistoryTableName))
            {
                throw new ArgumentNullException(nameof(storageAccountSetting), "EmailHistoryTableName property from StorageAccountSetting can't be null/empty. Please provide the value in appsettings.json file.");
            }

            if (string.IsNullOrEmpty(meetingHistoryTableName))
            {
                throw new ArgumentNullException(nameof(storageAccountSetting), "MeetingHistoryTableName property from StorageAccountSetting can't be null/empty. Please provide the value in appsettings.json file");
            }

            this.emailHistoryTable = this.cloudStorageClient.GetCloudTable(emailHistoryTableName);
            this.meetingHistoryTable = this.cloudStorageClient.GetCloudTable(meetingHistoryTableName);
            this.logger = logger ?? throw new System.ArgumentNullException(nameof(logger));
            this.mailAttachmentRepository = mailAttachmentRepository;
            this.cosmosLinqQuery = cosmosLinqQuery;
        }

        /// <inheritdoc/>
        public async Task CreateEmailNotificationItemEntities(IList<EmailNotificationItemEntity> emailNotificationItemEntities, string applicationName = null)
        {
            if (emailNotificationItemEntities is null || emailNotificationItemEntities.Count == 0)
            {
                throw new System.ArgumentNullException(nameof(emailNotificationItemEntities));
            }

            var traceProps = new Dictionary<string, string>();
            traceProps[AIConstants.Application] = applicationName;
            traceProps[AIConstants.EmailNotificationCount] = emailNotificationItemEntities?.Count.ToString(CultureInfo.InvariantCulture);

            this.logger.TraceInformation($"Started {nameof(this.CreateEmailNotificationItemEntities)} method of {nameof(TableStorageEmailRepository)}.", traceProps);
            IList<EmailNotificationItemEntity> updatedEmailNotificationItemEntities = emailNotificationItemEntities;
            if (applicationName != null)
            {
                updatedEmailNotificationItemEntities = await this.mailAttachmentRepository.UploadEmail(emailNotificationItemEntities, NotificationType.Mail.ToString(), applicationName).ConfigureAwait(false);
            }

            var batchesToCreate = SplitList((List<EmailNotificationItemEntity>)updatedEmailNotificationItemEntities, ApplicationConstants.BatchSizeToStore).ToList();

            foreach (var batch in batchesToCreate)
            {
                // TableBatchOperation batchOperation = new TableBatchOperation();
                List<TableTransactionAction> tableTransactionAction = new List<TableTransactionAction>();
                foreach (var item in batch)
                {
                    tableTransactionAction.Add(new TableTransactionAction(
                    TableTransactionActionType.Add,
                    item.ConvertToEmailNotificationItemTableEntity(),
                    ETag.All));
                }

                var response = await this.emailHistoryTable.SubmitTransactionAsync(tableTransactionAction).ConfigureAwait(false);

                if (response.GetRawResponse().IsError)
                {
                    throw new Exception($"Error while creating email notification entities in table storage. {response.Value[0].Status}");
                }
            }

            this.logger.TraceInformation($"Finished {nameof(this.CreateEmailNotificationItemEntities)} method of {nameof(TableStorageEmailRepository)}.", traceProps);

            return;
        }

        /// <inheritdoc/>
        public async Task<IList<EmailNotificationItemEntity>> GetEmailNotificationItemEntities(IList<string> notificationIds, string applicationName = null)
        {
            if (notificationIds is null || notificationIds.Count == 0)
            {
                throw new System.ArgumentNullException(nameof(notificationIds));
            }

            Expression<Func<EmailNotificationItemTableEntity, bool>> filterExpression = null;
            foreach (var item in notificationIds)
            {
                // filterExpression = filterExpression == null ? $"NotificationId eq {item}" : $" or NotificationId eq {item}";
                Expression<Func<EmailNotificationItemTableEntity, bool>> expression = a => a.NotificationId.Equals(item, StringComparison.InvariantCultureIgnoreCase);

                if (filterExpression != null)
                {
                    filterExpression = expression;
                }
                else
                {
                    filterExpression = ExpressionUtility.Or(filterExpression, expression);
                }
            }

            var traceProps = new Dictionary<string, string>();
            traceProps[AIConstants.Application] = applicationName;
            traceProps[AIConstants.NotificationIds] = JsonConvert.SerializeObject(notificationIds);

            this.logger.TraceInformation($"Started {nameof(this.GetEmailNotificationItemEntities)} method of {nameof(TableStorageEmailRepository)}.", traceProps);
            List<EmailNotificationItemTableEntity> emailNotificationItemEntities = new List<EmailNotificationItemTableEntity>();

            // var linqQuery = new TableQuery<EmailNotificationItemTableEntity>().Where(filterExpression);
            var emailNotificationItemEntitiesResponse = this.emailHistoryTable.QueryAsync(filterExpression).ConfigureAwait(false);

            await foreach (var item in emailNotificationItemEntitiesResponse)
            {
                emailNotificationItemEntities.Add(item);
            }

            // ExecuteQuery(linqQuery)?.Select(ent => ent).ToList();
            IList<EmailNotificationItemEntity> notificationEntities = emailNotificationItemEntities.Select(e => e.ConvertToEmailNotificationItemEntity()).ToList();
            IList<EmailNotificationItemEntity> updatedNotificationEntities = notificationEntities;
            if (applicationName != null)
            {
                updatedNotificationEntities = await this.mailAttachmentRepository.DownloadEmail(notificationEntities, applicationName).ConfigureAwait(false);
            }

            this.logger.TraceInformation($"Finished {nameof(this.GetEmailNotificationItemEntities)} method of {nameof(TableStorageEmailRepository)}.", traceProps);
            return updatedNotificationEntities;
        }

        /// <inheritdoc/>
        public async Task<EmailNotificationItemEntity> GetEmailNotificationItemEntity(string notificationId, string applicationName = null)
        {
            if (notificationId is null)
            {
                throw new System.ArgumentNullException(nameof(notificationId));
            }

            var traceprops = new Dictionary<string, string>();
            traceprops[AIConstants.Application] = applicationName;
            traceprops[AIConstants.NotificationIds] = notificationId;

            // string filterExpression = TableQuery.GenerateFilterCondition("NotificationId", QueryComparisons.Equal, notificationId);
            this.logger.TraceInformation($"Started {nameof(this.GetEmailNotificationItemEntity)} method of {nameof(TableStorageEmailRepository)}.", traceprops);
            List<EmailNotificationItemTableEntity> emailNotificationItemEntities = new List<EmailNotificationItemTableEntity>();

            // var linqQuery = new TableQuery<EmailNotificationItemTableEntity>().Where(filterExpression);
            var emailNotificationItemEntitiesResponse = this.emailHistoryTable.QueryAsync<EmailNotificationItemTableEntity>(a => a.NotificationId.Equals(notificationId, StringComparison.OrdinalIgnoreCase)).ConfigureAwait(false);

            await foreach (var item in emailNotificationItemEntitiesResponse)
            {
                emailNotificationItemEntities.Add(item);
            }

            // ExecuteQuery(linqQuery)?.Select(ent => ent).ToList();
            List<EmailNotificationItemEntity> notificationEntities = emailNotificationItemEntities.Select(e => e.ConvertToEmailNotificationItemEntity()).ToList();
            IList<EmailNotificationItemEntity> updatedNotificationEntities = notificationEntities;
            if (applicationName != null)
            {
                updatedNotificationEntities = await this.mailAttachmentRepository.DownloadEmail(notificationEntities, applicationName).ConfigureAwait(false);
            }

            this.logger.TraceInformation($"Finished {nameof(this.GetEmailNotificationItemEntity)} method of {nameof(TableStorageEmailRepository)}.", traceprops);
            if (updatedNotificationEntities.Count == 1)
            {
                return updatedNotificationEntities.FirstOrDefault();
            }
            else if (updatedNotificationEntities.Count > 1)
            {
                throw new ArgumentException("More than one entity found for the input notification id: ", notificationId);
            }
            else
            {
                throw new ArgumentException("No entity found for the input notification id: ", notificationId);
            }
        }

        /// <inheritdoc/>
        public async Task<Tuple<IList<EmailNotificationItemEntity>, string>> GetEmailNotifications(NotificationReportRequest notificationReportRequest)
        {
            if (notificationReportRequest == null)
            {
                throw new ArgumentNullException(nameof(notificationReportRequest));
            }

            this.logger.TraceInformation($"Started {nameof(this.GetEmailNotifications)} method of {nameof(TableStorageEmailRepository)}.");
            var entities = new List<EmailNotificationItemTableEntity>();
            var notificationEntities = new List<EmailNotificationItemEntity>();
            var filterDateExpression = this.GetDateFilterExpression(notificationReportRequest);
            var filterExpression = this.GetFilterExpression(notificationReportRequest);
            var finalFilter = filterDateExpression != null ? filterDateExpression : filterExpression;
            if (filterDateExpression != null && filterExpression != null)
            {
                // finalFilter = TableQuery.CombineFilters(filterDateExpression, TableOperators.And, filterExpression);
                finalFilter = ExpressionUtility.And(filterDateExpression, filterExpression);
            }

            // var tableQuery = new TableQuery<EmailNotificationItemTableEntity>()
            //         .Where(finalFilter)
            //         .OrderByDesc(notificationReportRequest.SendOnUtcDateStart)
            //         .Take(notificationReportRequest.Take == 0 ? 100 : notificationReportRequest.Take);
            // var queryResult = this.emailHistoryTable.ExecuteQuerySegmented(tableQuery, notificationReportRequest.Token);
            var result = this.emailHistoryTable.QueryAsync(finalFilter, maxPerPage: notificationReportRequest.Take > 0 ? notificationReportRequest.Take : 100);

            string continuationToken = string.Empty;
            await foreach (var page in result.AsPages())
            {
                entities.AddRange(page.Values.Select(item => new EmailNotificationItemTableEntity
                {
                    Application = item.Application,
                    BCC = item.BCC,
                    CC = item.CC,
                    EmailAccountUsed = item.EmailAccountUsed,
                    From = item.From,
                    ErrorMessage = item.ErrorMessage,
                    NotificationId = item.NotificationId,
                    Priority = item.Priority,
                    SendOnUtcDate = item.SendOnUtcDate,
                    ReplyTo = item.ReplyTo,
                    Subject = item.Subject,
                    Status = item.Status,
                    To = item.To,
                    TrackingId = item.TrackingId,
                    Timestamp = item.Timestamp,
                    TryCount = item.TryCount,
                    PartitionKey = item.PartitionKey,
                    RowKey = item.RowKey,
                    Sensitivity = Enum.Parse<MailSensitivity>(item.Sensitivity),
                    ETag = item.ETag,
                }));

                continuationToken = page.ContinuationToken;
            }

            notificationEntities = entities.Select(e => e.ConvertToEmailNotificationItemEntity()).ToList();
            Tuple<IList<EmailNotificationItemEntity>, string> tuple = new Tuple<IList<EmailNotificationItemEntity>, string>(notificationEntities, continuationToken);
            return tuple;

        }

        /// <inheritdoc/>
        public Task UpdateEmailNotificationItemEntities(IList<EmailNotificationItemEntity> emailNotificationItemEntities)
        {
            if (emailNotificationItemEntities is null || emailNotificationItemEntities.Count == 0)
            {
                throw new System.ArgumentNullException(nameof(emailNotificationItemEntities));
            }

            this.logger.TraceInformation($"Started {nameof(this.UpdateEmailNotificationItemEntities)} method of {nameof(TableStorageEmailRepository)}.");

            // TableBatchOperation batchOperation = new TableBatchOperation();
            // foreach (var item in emailNotificationItemEntities)
            // {
            //     batchOperation.Merge(item.ConvertToEmailNotificationItemTableEntity());
            // }
            // Task.WaitAll(this.emailHistoryTable.ExecuteBatchAsync(batchOperation));
            var tableTransactionAction = new List<TableTransactionAction>();

            foreach (var item in emailNotificationItemEntities)
            {
                tableTransactionAction.Add(new TableTransactionAction(TableTransactionActionType.UpsertReplace, item.ConvertToEmailNotificationItemTableEntity()));
            }

            _ = this.emailHistoryTable.SubmitTransaction(tableTransactionAction);

            try
            {
                List<Task> eventsDBUpdateTasks = new List<Task>();
                foreach (var item in emailNotificationItemEntities)
                {
                    // Fix this
                    var updatedItem = this.eventsContainer.GetItemLinqQueryable<EmailNotificationDto>(allowSynchronousQueryExecution: true)
                        .Where(nie => item.NotificationId == nie.ExternalId)
                        .AsEnumerable()
                        .FirstOrDefault();
                    updatedItem.Status.Code = (int)item.Status;
                    updatedItem.Status.Name = item.Status.ToString();
                    updatedItem.Status.LastModifiedOn = DateTime.Now;
                    updatedItem.Status.Description = $"This notification {item.Status}";
                    eventsDBUpdateTasks.Add(this.eventsContainer.UpsertItemAsync(updatedItem));
                }

                Task.WaitAll(eventsDBUpdateTasks.ToArray());
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception)
#pragma warning restore CA1031 // Do not catch general exception types
            {
            }

            this.logger.TraceInformation($"Finished {nameof(this.UpdateEmailNotificationItemEntities)} method of {nameof(TableStorageEmailRepository)}.");

            return Task.FromResult(true);
        }

        /// <inheritdoc/>
        public async Task<IList<MeetingNotificationItemEntity>> GetMeetingNotificationItemEntities(IList<string> notificationIds, string applicationName)
        {
            if (notificationIds is null || notificationIds.Count == 0)
            {
                throw new System.ArgumentNullException(nameof(notificationIds));
            }

            string filterExpression = null;
            foreach (var item in notificationIds)
            {
                filterExpression = filterExpression == null ? $"NotificationId eq {item}" : $" or NotificationId eq {item}";
            }

            var traceProps = new Dictionary<string, string>();
            traceProps[AIConstants.Application] = applicationName;
            traceProps[AIConstants.NotificationIds] = JsonConvert.SerializeObject(notificationIds);

            this.logger.TraceInformation($"Started {nameof(this.GetEmailNotificationItemEntities)} method of {nameof(TableStorageEmailRepository)}.", traceProps);
            List<MeetingNotificationItemTableEntity> meetingNotificationItemEntities = new List<MeetingNotificationItemTableEntity>();

            // var linqQuery = new TableQuery<MeetingNotificationItemTableEntity>().Where(filterExpression);

            // meetingNotificationItemEntities = this.meetingHistoryTable.ExecuteQuery(linqQuery)?.Select(ent => ent).ToList();
            var meetingNotificationItemEntitiesResponse = this.meetingHistoryTable.QueryAsync<MeetingNotificationItemTableEntity>(filterExpression).ConfigureAwait(false);

            await foreach (var item in meetingNotificationItemEntitiesResponse)
            {
                meetingNotificationItemEntities.Add(item);
            }

            IList<MeetingNotificationItemEntity> notificationEntities = meetingNotificationItemEntities.Select(e => e.ConvertToMeetingNotificationItemEntity()).ToList();
            IList<MeetingNotificationItemEntity> updatedNotificationEntities = await this.mailAttachmentRepository.DownloadMeetingInvite(notificationEntities, applicationName).ConfigureAwait(false);
            this.logger.TraceInformation($"Finished {nameof(this.GetEmailNotificationItemEntities)} method of {nameof(TableStorageEmailRepository)}.", traceProps);
            return updatedNotificationEntities;
        }

        /// <inheritdoc/>
        public async Task<MeetingNotificationItemEntity> GetMeetingNotificationItemEntity(string notificationId, string applicationName)
        {
            if (notificationId is null)
            {
                throw new System.ArgumentNullException(nameof(notificationId));
            }

            var traceProps = new Dictionary<string, string>();
            traceProps[AIConstants.NotificationIds] = notificationId;

            // string filterExpression = TableQuery.GenerateFilterCondition("NotificationId", QueryComparisons.Equal, notificationId);
            this.logger.TraceInformation($"Started {nameof(this.GetEmailNotificationItemEntity)} method of {nameof(TableStorageEmailRepository)}.", traceProps);
            List<MeetingNotificationItemTableEntity> meetingNotificationItemEntities = new List<MeetingNotificationItemTableEntity>();

            // var linqQuery = new TableQuery<MeetingNotificationItemTableEntity>().Where(filterExpression);
            // meetingNotificationItemEntities = this.meetingHistoryTable.ExecuteQuery(linqQuery)?.Select(ent => ent).ToList();
            var meetingNotificationEntitiesResponse = this.meetingHistoryTable.QueryAsync<MeetingNotificationItemTableEntity>(a => a.NotificationId.Equals(notificationId, StringComparison.OrdinalIgnoreCase)).ConfigureAwait(false);

            await foreach (var entity in meetingNotificationEntitiesResponse)
            {
                meetingNotificationItemEntities.Add(entity);
            }

            List<MeetingNotificationItemEntity> notificationEntities = meetingNotificationItemEntities.Select(e => e.ConvertToMeetingNotificationItemEntity()).ToList();
            IList<MeetingNotificationItemEntity> updatedNotificationEntities = await this.mailAttachmentRepository.DownloadMeetingInvite(notificationEntities, applicationName).ConfigureAwait(false);
            this.logger.TraceInformation($"Finished {nameof(this.GetEmailNotificationItemEntity)} method of {nameof(TableStorageEmailRepository)}.", traceProps);
            if (updatedNotificationEntities.Count == 1)
            {
                return updatedNotificationEntities.FirstOrDefault();
            }
            else if (updatedNotificationEntities.Count > 1)
            {
                throw new ArgumentException("More than one entity found for the input notification id: ", notificationId);
            }
            else
            {
                throw new ArgumentException("No entity found for the input notification id: ", notificationId);
            }
        }

        /// <summary>
        /// Creates the meeting notification item entities.
        /// </summary>
        /// <param name="meetingNotificationItemEntities">The meeting notification item entities.</param>
        /// <param name="applicationName"> application name as container name. </param>
        /// <returns>A <see cref="Task"/>.</returns>
        /// <exception cref="System.ArgumentNullException">meetingNotificationItemEntities.</exception>
        public async Task CreateMeetingNotificationItemEntities(IList<MeetingNotificationItemEntity> meetingNotificationItemEntities, string applicationName)
        {
            if (meetingNotificationItemEntities is null || meetingNotificationItemEntities.Count == 0)
            {
                throw new System.ArgumentNullException(nameof(meetingNotificationItemEntities));
            }

            var traceProps = new Dictionary<string, string>();
            traceProps[AIConstants.Application] = applicationName;
            traceProps[AIConstants.MeetingNotificationCount] = meetingNotificationItemEntities?.Count.ToString(CultureInfo.InvariantCulture);

            this.logger.TraceInformation($"Started {nameof(this.CreateMeetingNotificationItemEntities)} method of {nameof(TableStorageEmailRepository)}.", traceProps);
            IList<MeetingNotificationItemEntity> updatedEmailNotificationItemEntities = await this.mailAttachmentRepository.UploadMeetingInvite(meetingNotificationItemEntities, applicationName).ConfigureAwait(false);

            var batchesToCreate = SplitList((List<MeetingNotificationItemEntity>)updatedEmailNotificationItemEntities, ApplicationConstants.BatchSizeToStore).ToList();

            foreach (var batch in batchesToCreate)
            {
                // TableBatchOperation batchOperation = new TableBatchOperation();
                var transactionActions = new List<TableTransactionAction>();
                foreach (var item in batch)
                {
                    transactionActions.Add(new TableTransactionAction(TableTransactionActionType.Add, item.ConvertToMeetingNotificationItemTableEntity()));

                    // batchOperation.Insert(item.ConvertToMeetingNotificationItemTableEntity());
                }

                // Task.WaitAll(this.meetingHistoryTable.ExecuteBatchAsync(batchOperation));
                _ = await this.meetingHistoryTable.SubmitTransactionAsync(transactionActions).ConfigureAwait(false);
            }

            this.logger.TraceInformation($"Finished {nameof(this.CreateEmailNotificationItemEntities)} method of {nameof(TableStorageEmailRepository)}.", traceProps);
            return;
        }

        /// <inheritdoc/>
        public Task UpdateMeetingNotificationItemEntities(IList<MeetingNotificationItemEntity> meetingNotificationItemEntities)
        {
            if (meetingNotificationItemEntities is null || meetingNotificationItemEntities.Count == 0)
            {
                throw new System.ArgumentNullException(nameof(meetingNotificationItemEntities));
            }

            this.logger.TraceInformation($"Started {nameof(this.UpdateEmailNotificationItemEntities)} method of {nameof(TableStorageEmailRepository)}.");

            // TableBatchOperation batchOperation = new TableBatchOperation();
            // foreach (var item in meetingNotificationItemEntities)
            // {
            //    batchOperation.Merge(item.ConvertToMeetingNotificationItemTableEntity());
            // }
            // Task.WaitAll(this.meetingHistoryTable.ExecuteBatchAsync(batchOperation));

            // TableBatchOperation batchOperation = new TableBatchOperation();
            var transactionActions = new List<TableTransactionAction>();
            foreach (var item in meetingNotificationItemEntities)
            {
                transactionActions.Add(new TableTransactionAction(TableTransactionActionType.Add, item.ConvertToMeetingNotificationItemTableEntity()));

                // batchOperation.Insert(item.ConvertToMeetingNotificationItemTableEntity());
            }

            // Task.WaitAll(this.meetingHistoryTable.ExecuteBatchAsync(batchOperation));
            _ = this.meetingHistoryTable.SubmitTransaction(transactionActions);

            try
            {
                List<Task> eventsDBUpdateTasks = new List<Task>();
                foreach (var item in meetingNotificationItemEntities)
                {
                    var updatedItem = this.eventsContainer.GetItemLinqQueryable<EmailNotificationDto>(allowSynchronousQueryExecution: true)
                        .Where(nie => item.NotificationId == nie.ExternalId)
                        .AsEnumerable()
                        .FirstOrDefault();
                    updatedItem.Status.Code = (int)item.Status;
                    updatedItem.Status.Name = item.Status.ToString();
                    updatedItem.Status.LastModifiedOn = DateTime.Now;
                    updatedItem.Status.Description = $"This notification {item.Status}";
                    eventsDBUpdateTasks.Add(this.eventsContainer.UpsertItemAsync(updatedItem));
                }

                Task.WaitAll(eventsDBUpdateTasks.ToArray());
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception)
#pragma warning restore CA1031 // Do not catch general exception types
            {
            }

            this.logger.TraceInformation($"Finished {nameof(this.UpdateEmailNotificationItemEntities)} method of {nameof(TableStorageEmailRepository)}.");

            return Task.FromResult(true);
        }

        /// <inheritdoc/>
        public async Task<Tuple<IList<MeetingNotificationItemEntity>, string>> GetMeetingInviteNotifications(NotificationReportRequest meetingInviteReportRequest)
        {
            if (meetingInviteReportRequest == null)
            {
                throw new ArgumentNullException(nameof(meetingInviteReportRequest));
            }

            this.logger.TraceInformation($"Started {nameof(this.GetMeetingInviteNotifications)} method of {nameof(TableStorageEmailRepository)}.");
            var entities = new List<MeetingNotificationItemTableEntity>();
            var notificationEntities = new List<MeetingNotificationItemEntity>();
            var filterDateExpression = this.GetDateFilterExpression(meetingInviteReportRequest);
            var filterExpression = this.GetFilterExpression(meetingInviteReportRequest);
            var finalFilter = filterDateExpression != null ? filterDateExpression : filterExpression;
            if (filterDateExpression != null && filterExpression != null)
            {
                // finalFilter = TableQuery.CombineFilters(filterDateExpression, TableOperators.And, filterExpression);
                finalFilter = ExpressionUtility.And(filterDateExpression, filterExpression);
            }

            // var tableQuery = new TableQuery<MeetingNotificationItemTableEntity>()
            //         .Where(finalFilter)
            //         .OrderByDesc(meetingInviteReportRequest.SendOnUtcDateStart)
            //         .Take(meetingInviteReportRequest.Take == 0 ? 100 : meetingInviteReportRequest.Take);
            // var queryResult = this.meetingHistoryTable.ExecuteQuerySegmented(tableQuery, meetingInviteReportRequest.Token);
            var result = this.meetingHistoryTable.QueryAsync(finalFilter, maxPerPage: meetingInviteReportRequest.Take > 0 ? meetingInviteReportRequest.Take : 100);

            string token = string.Empty;
            await foreach (var page in result.AsPages(meetingInviteReportRequest.Token))
            {
                entities.AddRange(page.Values.Select(a => new MeetingNotificationItemTableEntity
                {
                    Application = a.Application,
                    NotificationId = a.NotificationId,
                    TrackingId = a.TrackingId,
                    From = a.From,
                    Timestamp = a.Timestamp,
                    SendOnUtcDate = a.SendOnUtcDate,
                    Subject = a.Subject,
                    Status = a.Status,
                }));

                token = page.ContinuationToken;
            }

            notificationEntities = entities.Select(e => e.ConvertToMeetingNotificationItemEntity()).ToList();
            Tuple<IList<MeetingNotificationItemEntity>, string> tuple = new Tuple<IList<MeetingNotificationItemEntity>, string>(notificationEntities, token);
            return tuple;
        }

        /// <inheritdoc/>
        public async Task<IList<EmailNotificationItemEntity>> GetPendingOrFailedEmailNotificationsByDateRange(DateTimeRange dateRange, string applicationName, List<NotificationItemStatus> statusList, bool loadBody = false)
        {
            if (dateRange == null || dateRange.StartDate == null || dateRange.EndDate == null)
            {
                throw new ArgumentNullException(nameof(dateRange));
            }

            // string filterExpression = $"SendOnUtcDate ge datetime'{dateRange.StartDate}' and SendOnUtcDate le datetime'{dateRange.EndDate}'";
            Expression<Func<EmailNotificationItemEntity, bool>> predicate = a => a.SendOnUtcDate >= dateRange.StartDate && a.SendOnUtcDate < dateRange.EndDate;

            if (!string.IsNullOrEmpty(applicationName))
            {
                Expression<Func<EmailNotificationItemEntity, bool>> applicationNamePredicate = a => a.Application.Equals(applicationName, StringComparison.InvariantCultureIgnoreCase);

                // filterExpression += " and Application eq {applicationName}";
                predicate = ExpressionUtility.And(predicate, applicationNamePredicate);
            }

            if (statusList != null && statusList.Count > 0)
            {
                Expression<Func<EmailNotificationItemEntity, bool>> statusFilterExpression = null;
                foreach (var status in statusList)
                {
                    // string filter = $"Status eq {status}";
                    Expression<Func<EmailNotificationItemEntity, bool>> statusPredicate = a => a.Status == status;

                    // statusFilterExpression = statusFilterExpression == null ? filter : " or " + filter;
                    statusFilterExpression = statusFilterExpression == null ? statusPredicate : ExpressionUtility.Or(statusFilterExpression, statusPredicate);
                }

                // filterExpression = TableQuery.CombineFilters(filterExpression, TableOperators.And, statusFilterExpression);
                predicate = ExpressionUtility.And(predicate, statusFilterExpression);
            }

            var traceProps = new Dictionary<string, string>();
            traceProps[AIConstants.Application] = applicationName;
            traceProps[AIConstants.ResendDateRange] = JsonConvert.SerializeObject(dateRange);

            this.logger.TraceInformation($"Started {nameof(this.GetPendingOrFailedEmailNotificationsByDateRange)} method of {nameof(TableStorageEmailRepository)}.", traceProps);
            List<EmailNotificationItemTableEntity> emailNotificationItemEntities = new List<EmailNotificationItemTableEntity>();

            // var linqQuery = new TableQuery<EmailNotificationItemTableEntity>().Where(filterExpression);
            // emailNotificationItemEntities = this.emailHistoryTable.ExecuteQuery(linqQuery)?.Select(ent => ent).ToList();
            var result = this.emailHistoryTable.QueryAsync(predicate);

            await foreach (var item in result)
            {
                emailNotificationItemEntities.Add(new EmailNotificationItemTableEntity
                {
                    Application = item.Application,
                    BCC = item.BCC,
                    CC = item.CC,
                    EmailAccountUsed = item.EmailAccountUsed,
                    From = item.From,
                    ErrorMessage = item.ErrorMessage,
                    NotificationId = item.NotificationId,
                    Priority = item.Priority.ToString(),
                    SendOnUtcDate = item.SendOnUtcDate,
                    ReplyTo = item.ReplyTo,
                    Subject = item.Subject,
                    Status = item.Status.ToString(),
                    To = item.To,
                    TrackingId = item.TrackingId,
                    Timestamp = item.Timestamp,
                    TryCount = item.TryCount,
                    PartitionKey = item.PartitionKey,
                    RowKey = item.RowKey,
                    Sensitivity = item.Sensitivity,
                    ETag = item.ETag,
                    TemplateId = item.TemplateId,
                });
            }

            if (emailNotificationItemEntities == null || emailNotificationItemEntities.Count == 0)
            {
                return null;
            }

            IList<EmailNotificationItemEntity> notificationEntities = emailNotificationItemEntities.Select(e => e.ConvertToEmailNotificationItemEntity()).ToList();
            IList<EmailNotificationItemEntity> updatedNotificationEntities = notificationEntities;
            if (!string.IsNullOrEmpty(applicationName) && loadBody)
            {
                updatedNotificationEntities = await this.mailAttachmentRepository.DownloadEmail(notificationEntities, applicationName).ConfigureAwait(false);
            }

            this.logger.TraceInformation($"Finished {nameof(this.GetPendingOrFailedEmailNotificationsByDateRange)} method of {nameof(TableStorageEmailRepository)}.", traceProps);
            return updatedNotificationEntities;
        }

        /// <inheritdoc/>
        public async Task<IList<MeetingNotificationItemEntity>> GetPendingOrFailedMeetingNotificationsByDateRange(DateTimeRange dateRange, string applicationName, List<NotificationItemStatus> statusList, bool loadBody = false)
        {
            if (dateRange == null || dateRange.StartDate == null || dateRange.EndDate == null)
            {
                throw new ArgumentNullException(nameof(dateRange));
            }

            // string filterExpression = TableQuery.GenerateFilterConditionForDate("SendOnUtcDate", QueryComparisons.GreaterThanOrEqual, dateRange.StartDate)
            //         + " and "
            //         + TableQuery.GenerateFilterConditionForDate("SendOnUtcDate", QueryComparisons.LessThan, dateRange.EndDate);
            // if (!string.IsNullOrEmpty(applicationName))
            // {
            //    filterExpression = filterExpression
            //        + " and "
            //        + TableQuery.GenerateFilterCondition("Application", QueryComparisons.Equal, applicationName);
            // }
            // if (statusList != null && statusList.Count > 0)
            // {
            //    string statusFilterExpression = null;
            //    foreach (var status in statusList)
            //    {
            //        string filter = TableQuery.GenerateFilterCondition("Status", QueryComparisons.Equal, status.ToString());
            //        statusFilterExpression = statusFilterExpression == null ? filter : " or " + filter;
            //    }
            //    filterExpression = TableQuery.CombineFilters(filterExpression, TableOperators.And, statusFilterExpression);
            // }
            Expression<Func<EmailNotificationItemEntity, bool>> predicate = a => a.SendOnUtcDate >= dateRange.StartDate && a.SendOnUtcDate < dateRange.EndDate;

            if (!string.IsNullOrEmpty(applicationName))
            {
                Expression<Func<EmailNotificationItemEntity, bool>> applicationNamePredicate = a => a.Application.Equals(applicationName, StringComparison.InvariantCultureIgnoreCase);

                // filterExpression += " and Application eq {applicationName}";
                predicate = ExpressionUtility.And(predicate, applicationNamePredicate);
            }

            if (statusList != null && statusList.Count > 0)
            {
                Expression<Func<EmailNotificationItemEntity, bool>> statusFilterExpression = null;
                foreach (var status in statusList)
                {
                    // string filter = $"Status eq {status}";
                    Expression<Func<EmailNotificationItemEntity, bool>> statusPredicate = a => a.Status == status;

                    // statusFilterExpression = statusFilterExpression == null ? filter : " or " + filter;
                    statusFilterExpression = statusFilterExpression == null ? statusPredicate : ExpressionUtility.Or(statusFilterExpression, statusPredicate);
                }

                // filterExpression = TableQuery.CombineFilters(filterExpression, TableOperators.And, statusFilterExpression);
                predicate = ExpressionUtility.And(predicate, statusFilterExpression);
            }

            var traceProps = new Dictionary<string, string>();
            traceProps[AIConstants.Application] = applicationName;
            traceProps[AIConstants.ResendDateRange] = JsonConvert.SerializeObject(dateRange);

            this.logger.TraceInformation($"Started {nameof(this.GetPendingOrFailedMeetingNotificationsByDateRange)} method of {nameof(TableStorageEmailRepository)}.", traceProps);
            List<MeetingNotificationItemTableEntity> meetingNotificationItemEntities = new List<MeetingNotificationItemTableEntity>();

            // var linqQuery = new TableQuery<MeetingNotificationItemTableEntity>().Where(filterExpression);
            // meetingNotificationItemEntities = this.meetingHistoryTable.ExecuteQuery(linqQuery)?.Select(ent => ent).ToList();
            var result = this.meetingHistoryTable.QueryAsync(predicate);

            await foreach (var item in result)
            {
                meetingNotificationItemEntities.Add(new MeetingNotificationItemTableEntity
                {
                    Application = item.Application,
                    NotificationId = item.NotificationId,
                    TrackingId = item.TrackingId,
                    From = item.From,
                    Timestamp = item.Timestamp,
                    SendOnUtcDate = item.SendOnUtcDate,
                    Subject = item.Subject,
                    Status = item.Status.ToString(),
                    EmailAccountUsed = item.EmailAccountUsed,
                    ErrorMessage = item.ErrorMessage,
                    TryCount = item.TryCount,
                    PartitionKey = item.PartitionKey,
                    RowKey = item.RowKey,
                    ETag = item.ETag,
                });
            }

            if (meetingNotificationItemEntities == null || meetingNotificationItemEntities.Count == 0)
            {
                return null;
            }

            IList<MeetingNotificationItemEntity> notificationEntities = meetingNotificationItemEntities.Select(e => e.ConvertToMeetingNotificationItemEntity()).ToList();
            IList<MeetingNotificationItemEntity> updatedNotificationEntities = notificationEntities;
            if (!string.IsNullOrEmpty(applicationName) && loadBody)
            {
                updatedNotificationEntities = await this.mailAttachmentRepository.DownloadMeetingInvite(notificationEntities, applicationName).ConfigureAwait(false);
            }

            this.logger.TraceInformation($"Finished {nameof(this.GetPendingOrFailedMeetingNotificationsByDateRange)} method of {nameof(TableStorageEmailRepository)}.", traceProps);
            return updatedNotificationEntities;
        }

        /// <summary>
        /// Get notification status string.
        /// </summary>
        /// <param name="status">notification status int format.</param>
        /// <returns>returns notification status string. </returns>
        private static string GetStatus(int status)
        {
            string statusStr = "Queued";
            switch (status)
            {
                case 0:
                    statusStr = "Queued";
                    break;
                case 1:
                    statusStr = "Processing";
                    break;
                case 2:
                    statusStr = "Retrying";
                    break;
                case 3:
                    statusStr = "Failed";
                    break;
                case 4:
                    statusStr = "Sent";
                    break;
                case 5:
                    statusStr = "FakeMail";
                    break;
            }

            return statusStr;
        }

#pragma warning disable CA1822 // Mark members as static
        private Expression<Func<NotificationReportResponse, bool>> GetFilterExpression(NotificationReportRequest notificationReportRequest)
#pragma warning restore CA1822 // Mark members as static
        {
            var filterSet = new HashSet<Expression<Func<NotificationReportResponse, bool>>>();
            Expression<Func<NotificationReportResponse, bool>> filterExpression = null;
            string applicationFilter = null;
            string accountFilter = null;
            string notificationFilter = null;
            string statusFilter = null;
            string trackingIdFilter = null;

            if (notificationReportRequest.ApplicationFilter?.Count > 0)
            {
                Expression<Func<NotificationReportResponse, bool>> expression = null;
                foreach (var item in notificationReportRequest.ApplicationFilter)
                {
                    // applicationFilter = applicationFilter == null ? TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, item) : applicationFilter + " or " + TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, item);
                    Expression<Func<NotificationReportResponse, bool>> partitionExpression = a => a.PartitionKey == item;
                    if (expression == null)
                    {
                        expression = partitionExpression;
                    }
                    else
                    {
                        expression = ExpressionUtility.Or(expression, partitionExpression);
                    }
                }

                _ = filterSet.Add(expression);
            }

            if (notificationReportRequest.AccountsUsedFilter?.Count > 0)
            {
                Expression<Func<NotificationReportResponse, bool>> expression = null;
                foreach (var item in notificationReportRequest.AccountsUsedFilter)
                {
                    // accountFilter = accountFilter == null ? TableQuery.GenerateFilterCondition("EmailAccountUsed", QueryComparisons.Equal, item) : accountFilter + " or " + TableQuery.GenerateFilterCondition("EmailAccountUsed", QueryComparisons.Equal, item);
                    Expression<Func<NotificationReportResponse, bool>> emailAccountUsedExpression = a => a.EmailAccountUsed == item;

                    if (expression == null)
                    {
                        expression = emailAccountUsedExpression;
                    }
                    else
                    {
                        expression = ExpressionUtility.Or(expression, emailAccountUsedExpression);
                    }
                }

                _ = filterSet.Add(expression);
            }

            if (notificationReportRequest.NotificationIdsFilter?.Count > 0)
            {
                Expression<Func<NotificationReportResponse, bool>> expression = null;

                foreach (var item in notificationReportRequest.NotificationIdsFilter)
                {
                    // notificationFilter = notificationFilter == null ? TableQuery.GenerateFilterCondition("NotificationId", QueryComparisons.Equal, item) : notificationFilter + " or " + TableQuery.GenerateFilterCondition("NotificationId", QueryComparisons.Equal, item);
                    Expression<Func<NotificationReportResponse, bool>> emailAccountUsedExpression = a => a.NotificationId == item;

                    if (expression == null)
                    {
                        expression = emailAccountUsedExpression;
                    }
                    else
                    {
                        expression = ExpressionUtility.Or(expression, emailAccountUsedExpression);
                    }
                }

                _ = filterSet.Add(expression);
            }

            if (notificationReportRequest.TrackingIdsFilter?.Count > 0)
            {
                Expression<Func<NotificationReportResponse, bool>> expression = null;

                foreach (var item in notificationReportRequest.TrackingIdsFilter)
                {
                    // trackingIdFilter = trackingIdFilter == null ? TableQuery.GenerateFilterCondition("TrackingId", QueryComparisons.Equal, item) : trackingIdFilter + " or " + TableQuery.GenerateFilterCondition("TrackingId", QueryComparisons.Equal, item);
                    Expression<Func<NotificationReportResponse, bool>> emailAccountUsedExpression = a => a.TrackingId == item;

                    if (expression == null)
                    {
                        expression = emailAccountUsedExpression;
                    }
                    else
                    {
                        expression = ExpressionUtility.Or(expression, emailAccountUsedExpression);
                    }
                }

                _ = filterSet.Add(expression);
            }

            if (notificationReportRequest.NotificationStatusFilter?.Count > 0)
            {
                Expression<Func<NotificationReportResponse, bool>> expression = null;

                foreach (int item in notificationReportRequest.NotificationStatusFilter)
                {
                    string status = GetStatus(item);
                    // statusFilter = statusFilter == null ? TableQuery.GenerateFilterCondition("Status", QueryComparisons.Equal, status) : statusFilter + " or " + TableQuery.GenerateFilterCondition("Status", QueryComparisons.Equal, status);
                    Expression<Func<NotificationReportResponse, bool>> statusExpression = a => a.Status == status;

                    if (expression == null)
                    {
                        expression = statusExpression;
                    }
                    else
                    {
                        expression = ExpressionUtility.Or(expression, statusExpression);
                    }
                }

                _ = filterSet.Add(expression);
            }

            filterExpression = PrepareFilterExp(filterSet);
            return filterExpression;

            static Expression<Func<NotificationReportResponse, bool>> PrepareFilterExp(HashSet<Expression<Func<NotificationReportResponse, bool>>> filterSet)
            {
                Expression<Func<NotificationReportResponse, bool>> filterExp = null;
                foreach (var filter in filterSet)
                {
                    filterExp = ExpressionUtility.And(filterExp, filter);
                }

                return filterExp;
            }
        }

#pragma warning disable CA1822 // Mark members as static
        private Expression<Func<NotificationReportResponse, bool>> GetDateFilterExpression(NotificationReportRequest notificationReportRequest)
#pragma warning restore CA1822 // Mark members as static
        {
            Expression<Func<NotificationReportResponse, bool>> expression = null;
            if (DateTime.TryParse(notificationReportRequest.CreatedDateTimeStart, out DateTime createdDateTimeStart))
            {
                // filterExpression = filterExpression == null ? TableQuery.GenerateFilterConditionForDate("CreatedDateTimeStart", QueryComparisons.GreaterThanOrEqual, createdDateTimeStart) : filterExpression + TableQuery.GenerateFilterConditionForDate("CreatedDateTimeStart", QueryComparisons.GreaterThanOrEqual, createdDateTimeStart);
                Expression<Func<NotificationReportResponse, bool>> dateExpression = a => DateTime.Compare(a.CreatedDateTime, createdDateTimeStart) >= 0;

                if (expression == null)
                {
                    expression = dateExpression;
                }
                else
                {
                    expression = ExpressionUtility.And(expression, dateExpression);
                }
            }

            if (DateTime.TryParse(notificationReportRequest.CreatedDateTimeEnd, out DateTime createdDateTimeEnd))
            {
                // filterExpression = filterExpression == null ? TableQuery.GenerateFilterConditionForDate("CreatedDateTimeEnd", QueryComparisons.LessThanOrEqual, createdDateTimeEnd) : filterExpression + TableQuery.GenerateFilterConditionForDate("CreatedDateTimeEnd", QueryComparisons.LessThanOrEqual, createdDateTimeEnd);
                Expression<Func<NotificationReportResponse, bool>> dateExpression = a => DateTime.Compare(a.CreatedDateTime, createdDateTimeEnd) <= 0;
                if (expression == null)
                {
                    expression = dateExpression;
                }
                else
                {
                    expression = ExpressionUtility.And(expression, dateExpression);
                }
            }

            if (DateTime.TryParse(notificationReportRequest.SendOnUtcDateStart, out DateTime sentTimeStart))
            {
                // filterExpression = filterExpression == null ? TableQuery.GenerateFilterConditionForDate("SendOnUtcDate", QueryComparisons.GreaterThanOrEqual, sentTimeStart) : filterExpression + " and " + TableQuery.GenerateFilterConditionForDate("SendOnUtcDate", QueryComparisons.GreaterThanOrEqual, sentTimeStart
                Expression<Func<NotificationReportResponse, bool>> dateExpression = a => DateTime.Compare(a.SendOnUtcDate, sentTimeStart) >= 0;
                if (expression == null)
                {
                    expression = dateExpression;
                }
                else
                {
                    expression = ExpressionUtility.And(expression, dateExpression);
                }
            }

            if (DateTime.TryParse(notificationReportRequest.SendOnUtcDateEnd, out DateTime sentTimeEnd))
            {
                // filterExpression = filterExpression == null ? TableQuery.GenerateFilterConditionForDate("SendOnUtcDate", QueryComparisons.LessThanOrEqual, sentTimeEnd) : filterExpression + " and " + TableQuery.GenerateFilterConditionForDate("SendOnUtcDate", QueryComparisons.LessThanOrEqual, sentTimeEnd);
                Expression<Func<NotificationReportResponse, bool>> dateExpression = a => DateTime.Compare(a.SendOnUtcDate, sentTimeEnd) <= 0;
                if (expression == null)
                {
                    expression = dateExpression;
                }
                else
                {
                    expression = ExpressionUtility.And(expression, dateExpression);
                }
            }

            if (DateTime.TryParse(notificationReportRequest.UpdatedDateTimeStart, out DateTime updatedTimeStart))
            {
                // filterExpression = filterExpression == null ? TableQuery.GenerateFilterConditionForDate("UpdatedDateTimeStart", QueryComparisons.GreaterThanOrEqual, updatedTimeStart) : filterExpression + TableQuery.GenerateFilterConditionForDate("UpdatedDateTimeStart", QueryComparisons.GreaterThanOrEqual, updatedTimeStart); ;
                Expression<Func<NotificationReportResponse, bool>> dateExpression = a => DateTime.Compare(a.UpdatedDateTime, updatedTimeStart) < 0;
                if (expression == null)
                {
                    expression = dateExpression;
                }
                else
                {
                    expression = ExpressionUtility.And(expression, dateExpression);
                }
            }

            if (DateTime.TryParse(notificationReportRequest.UpdatedDateTimeEnd, out DateTime updatedTimeEnd))
            {
                // filterExpression = filterExpression == null ? TableQuery.GenerateFilterConditionForDate("UpdatedDateTimeEnd", QueryComparisons.LessThanOrEqual, updatedTimeEnd) : filterExpression + TableQuery.GenerateFilterConditionForDate("UpdatedDateTimeEnd", QueryComparisons.LessThanOrEqual, updatedTimeEnd);
                Expression<Func<NotificationReportResponse, bool>> dateExpression = a => DateTime.Compare(a.UpdatedDateTime, updatedTimeEnd) > 0;
                if (expression == null)
                {
                    expression = dateExpression;
                }
                else
                {
                    expression = ExpressionUtility.And(expression, dateExpression);
                }
            }

            return expression;
        }

        /// <summary>
        /// Breaks the input list to multiple chunks each of size provided as input.
        /// </summary>
        /// <typeparam name="T">Type of object in the List.</typeparam>
        /// <param name="listItems">List of objects.</param>
        /// <param name="nSize">Chunk size.</param>
        /// <returns>An enumerable collection of chunks.</returns>
        private static IEnumerable<IList<T>> SplitList<T>(List<T> listItems, int nSize = 4)
        {
            if (listItems is null)
            {
                throw new ArgumentNullException(nameof(listItems));
            }

            for (int i = 0; i < listItems.Count; i += nSize)
            {
                yield return listItems.GetRange(i, Math.Min(nSize, listItems.Count - i));
            }
        }
    }
}
