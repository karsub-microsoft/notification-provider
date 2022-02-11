// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace NotificationService.Contracts
{
    using System;

    /// <summary>
    /// Meeting invite details
    /// </summary>
    public class MeetingInviteNotificationDto: EmailNotificationDto
    {
        public new string NotificationType = "MeetingInvite";
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public bool IsAllDay { get; set; }
        public string Location { get; set; }
        public bool IsOnline { get; set; }
        public bool RequireResponse { get; set; }
        public int RemindBefore { get; set; }
        public string ShowAs { get; set; }
        public MeetingRecurrenceDto MeetingRecurrence { get; set; }
        public bool AllowNewTimeProposals { get; set; }
        public string ICalUid { get; set; }
        public bool CancelInvite { get; set; }
    }
}
