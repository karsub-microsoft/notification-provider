// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace NotificationService.Contracts.Entities
{
    using Azure;
    using Azure.Data.Tables;
    using System;
    using System.Runtime.Serialization;

    /// <summary>
    /// Mail template entity.
    /// </summary>
    [DataContract]
    public class MailTemplateEntity : ITableEntity
    {
        /// <summary>
        /// Gets or sets the template name.
        /// </summary>
        [DataMember(Name = "TemplateId")]
        public string TemplateId { get; set; }

        /// <summary>
        /// Gets or sets the template description.
        /// </summary>
        [DataMember(Name = "description")]
        public string Description { get; set; }

        /// <summary>
        /// Gets or sets the template content.
        /// </summary>
        [DataMember(Name = "content")]
        public string Content { get; set; }

        /// <summary>
        /// Gets or sets the template type.
        /// </summary>
        [DataMember(Name = "templateType")]
        public string TemplateType { get; set; }

        /// <summary>
        /// Gets or sets Application associated to the mail template item.
        /// </summary>
        [DataMember(Name = "Application")]
        public string Application { get; set; }
        public string PartitionKey { get; set; }
        public string RowKey { get; set ; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}
