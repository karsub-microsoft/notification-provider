// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace NotificationService.Data
{
    using Azure.Data.Tables;

    /// <summary>
    /// Interface to Azure Cloud Storage.
    /// </summary>
    public interface ITableStorageClient
    {
        /// <summary>
        /// Gets an instance of <see cref="CloudTable"/> for the input queue name.
        /// </summary>
        /// <param name="tableName">Name of queue.</param>
        /// <returns><see cref="CloudTable"/>.</returns>
        TableClient GetCloudTable(string tableName);
    }
}
