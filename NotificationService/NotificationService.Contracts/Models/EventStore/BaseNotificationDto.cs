// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace NotificationService.Contracts
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Newtonsoft.Json;

    /// <summary>
    /// Common user notification
    /// </summary>
    public class BaseNotificationDto
    {
        [JsonProperty("id")]
        public string NotificationId { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string EventId { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string EventName { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string ExternalId { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string SenderAddress { get; set; }

        public string Subject { get; set; }

        public string Content { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<string> ReceiverAddresses { get; set; }

        public NotificationStatusDto Status { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string PublisherId { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, string> Properties { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, object> AdvancedRenderHash { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string Channel { get; set; }

        public string Priority { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string Sensitivity { get; set; }

        public bool EnableAdvancedRendering { get; set; }

        public bool IsOverriden { get; set; }

        public bool IsAutoResolved { get; set; }

        public DateTime PublishedOn { get; set; }

        public DateTime ExpireOn { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string Failure { get; set; }

        public int RetryCount { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public long ExpireOnTicks { get => ExpireOn.Ticks; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public long PublishedOnTicks { get => PublishedOn.Ticks; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<string> RejectedReceiverAddresses { get; set; }

        public bool AreInvalidUsersRejected { get => RejectedReceiverAddresses != null && RejectedReceiverAddresses.Any(); }
    }
}
