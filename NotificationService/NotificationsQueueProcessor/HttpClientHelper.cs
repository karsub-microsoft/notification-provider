// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace NotificationsQueueProcessor
{
    using System;
    using System.Linq;
    using System.Net.Http;
    using System.Security.Cryptography.X509Certificates;
    using System.Threading;
    using System.Threading.Tasks;
    using Azure.Core;
    using Azure.Identity;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Identity.Client;
    using Microsoft.IdentityModel.Clients.ActiveDirectory;
    using NotificationService.Common;

    /// <summary>
    /// HttpClientHelper.
    /// </summary>
    /// <seealso cref="NotificationsQueueProcessor.IHttpClientHelper" />
    public class HttpClientHelper : IHttpClientHelper
    {
        private readonly HttpClient httpClient;

        // Instance of Application Configuration.
        private readonly IConfiguration configuration;

        /// <summary>
        /// Initializes a new instance of the <see cref="HttpClientHelper"/> class.
        /// </summary>
        /// <param name="httpClient">The HTTP client.</param>
        /// <param name="configuration">The configuration.</param>
        public HttpClientHelper(HttpClient httpClient, IConfiguration configuration)
        {
            this.httpClient = httpClient;
            this.configuration = configuration;
            string httpTimeOut = this.configuration?[Constants.HttpTimeOutInSec];
            if (int.TryParse(httpTimeOut, out int timeOut))
            {
                this.httpClient.Timeout = TimeSpan.FromSeconds(timeOut);
            }
        }

        /// <summary>
        /// Posts the asynchronous.
        /// </summary>
        /// <param name="url">The URL.</param>
        /// <param name="content">The content.</param>
        /// <returns>A <see cref="Task"/>.</returns>
        public async Task<HttpResponseMessage> PostAsync(string url, HttpContent content)
        {
            string bearerToken = await this.HttpAuthenticationAsync(this.configuration?[Constants.Authority], this.configuration?[Constants.ClientId]).ConfigureAwait(false);
            if (bearerToken != null)
            {
                this.httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(ApplicationConstants.BearerAuthenticationScheme, bearerToken);
                return await this.httpClient.PostAsync(url, content).ConfigureAwait(false);
            }
            else
            {
                throw new Exception($"Unable to generate token for {this.configuration?[Constants.ClientId]} in ProcessNotificationQueueItem");
            }
        }

        private async Task<string> HttpAuthenticationAsync(string authority, string clientId)
        {
            var authContext = new AuthenticationContext(authority);

            // Change to FIC
#if !DEBUG
            var authResult = await GetTokenUsingFic(authority, clientId, clientId);
            return authResult;
#else
            var authResult = await GetTokenUsingSni(clientId, clientId, this.configuration["CertificateThumbprint"]).ConfigureAwait(false);
            return authResult;
#endif
        }

        private static async Task<string> GetTokenUsingSni(string clientId, string resourceId, string certificateThumbprint)
        {
            var certificate = await GetCertificate(certificateThumbprint).ConfigureAwait(false);

            IConfidentialClientApplication confidentialClientApplication = ConfidentialClientApplicationBuilder.Create(clientId)
                                                                            .WithAuthority(AzureCloudInstance.AzurePublic, "microsoft.onmicrosoft.com")
                                                                            .WithCertificate(certificate)
                                                                            .Build();

            var authResult = await confidentialClientApplication.AcquireTokenForClient(new[] { $"{resourceId}/.default" }).ExecuteAsync().ConfigureAwait(false);
            return $"{authResult.AccessToken}";
        }

        /// <summary>
        /// Get Certificate from device store.
        /// </summary>
        /// <param name="certificateThumbprint">Thumbprint of Certificate</param>
        /// <returns>Certificate</returns>
        internal static Task<X509Certificate2> GetCertificate(string certificateThumbprint)
        {
            using (var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine))
            {
                store.Open(OpenFlags.ReadOnly);
                var cert = store.Certificates.OfType<X509Certificate2>()
                    .FirstOrDefault(x => x.Thumbprint == certificateThumbprint);
                return Task.FromResult(cert);
            }
        }

        private static async Task<string> GetTokenUsingFic(string authority, string clientId, string resourceId)
        {
            IConfidentialClientApplication clientApplicationWithManagedIdentity = ConfidentialClientApplicationBuilder.Create(clientId).WithAuthority(new Uri(authority))
                   .WithClientAssertion((AssertionRequestOptions options) =>
                   {
                       DefaultAzureCredential defaultAzureCredential;

                       defaultAzureCredential = new DefaultAzureCredential();
                       var accessToken = defaultAzureCredential.GetToken(new TokenRequestContext(new string[] { resourceId }), CancellationToken.None);
                       return Task.FromResult(accessToken.Token);
                   }).Build();

            var authResult = await clientApplicationWithManagedIdentity.AcquireTokenForClient(new[] { $"{clientId}/.default" }).ExecuteAsync().ConfigureAwait(false);
            return $"{authResult.AccessToken}";
        }
    }
}
