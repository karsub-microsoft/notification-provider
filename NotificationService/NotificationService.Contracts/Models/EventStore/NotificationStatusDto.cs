// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace NotificationService.Contracts
{
    using System;

    /// <summary>
    /// Status of a notification
    /// </summary>
    public class NotificationStatusDto
    {
        public int Code { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public DateTime LastModifiedOn { get; set; }

        /// <inheritdoc/>
        public override string ToString() => $"{this.Code}:{this.Name}:{this.Description}:{this.LastModifiedOn}";
    }
}
