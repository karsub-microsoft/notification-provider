// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace NotificationService.UnitTests.BusinesLibrary.V1.EmailManager
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.Threading.Tasks;
    using Moq;
    using NotificationService.Contracts;
    using NotificationService.UnitTests.BusinessLibrary.V1.EmailManager;
    using NUnit.Framework;

    /// <summary>
    /// Tests for ResendEmailNotifications method of Email Manager.
    /// </summary>
    [ExcludeFromCodeCoverage]
    public class ResendEmailNotificationsTests : EmailManagerTestsBase
    {
        /// <summary>
        /// Initialization for the tests.
        /// </summary>
        [SetUp]
        public void Setup() => this.SetupTestBase();

        /// <summary>
        /// Tests for ResendEmailNotifications method for valid inputs.
        /// </summary>
        [Test]
        public void ResendEmailNotificationsTestValidInput()
        {
            Task<IList<NotificationResponse>> result = this.EmailHandlerManager.ResendNotifications(ApplicationName, new string[] { Guid.NewGuid().ToString() });
            Assert.AreEqual(result.Status.ToString(), "RanToCompletion");
            this.CloudStorageClient.Verify(csc => csc.GetCloudQueue(It.IsAny<string>()), Times.Once);
            this.CloudStorageClient.Verify(csc => csc.QueueCloudMessages(It.IsAny<IList<string>>(), null), Times.Once);
            Assert.Pass();
        }

        /// <summary>
        /// Tests for ResendEmailNotifications method for invalid inputs.
        /// </summary>
        [Test]
        public void ResendEmailNotificationsTestInvalidInput()
        {
            _ = Assert.ThrowsAsync<ArgumentException>(async () => await this.EmailHandlerManager.ResendNotifications(null, new string[] { Guid.NewGuid().ToString() }).ConfigureAwait(false));
            _ = Assert.ThrowsAsync<ArgumentNullException>(async () => await this.EmailHandlerManager.ResendNotifications(ApplicationName, null).ConfigureAwait(false));
        }
    }
}
