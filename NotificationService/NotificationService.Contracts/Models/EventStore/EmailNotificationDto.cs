// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace NotificationService.Contracts
{
    using System.Collections.Generic;
    using Newtonsoft.Json;

    /// <summary>
    /// Details of email to be sent to a user
    /// </summary>
    public class EmailNotificationDto : BaseNotificationDto
    {
        public string NotificationType = "Email";

        public List<string> AlternateReceiverAddreses { get; set; }

        public List<string> BlindReceiverAddreses { get; set; }

        public bool IsActionable { get; set; }
    }
}
