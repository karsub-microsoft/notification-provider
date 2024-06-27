// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace NotificationService.BusinessLibrary
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Azure.Core;
    using Azure.Identity;
    using Microsoft.Extensions.Options;
    using Microsoft.Identity.Client;
    using Newtonsoft.Json.Linq;
    using NotificationService.BusinessLibrary.Interfaces;
    using NotificationService.Common;
    using NotificationService.Common.Logger;
    using NotificationService.Contracts;

    /// <summary>
    /// Helper class to handle token related activities.
    /// </summary>
    public class TokenHelper : ITokenHelper
    {
        private const string BearerText = "Bearer ";

        /// <summary>
        /// User Token Configuration.
        /// </summary>
        private readonly UserTokenSetting userTokenSetting;

        /// <summary>
        /// MS Graph configuration.
        /// </summary>
        private readonly MSGraphSetting mSGraphSetting;

        /// <summary>
        /// Instance of <see cref="ILogger"/>.
        /// </summary>
        private readonly ILogger logger;

        private readonly IEmailAccountManager emailAccountManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="TokenHelper"/> class.
        /// </summary>
        /// <param name="userTokenSetting">User token setting from configuration.</param>
        /// <param name="mSGraphSetting">MS Graph Settings from configuration.</param>
        /// <param name="logger">Instance of <see cref="ILogger"/>.</param>
        /// <param name="emailAccountManager">Instance of <see cref="IEmailAccountManager"/>.</param>
        public TokenHelper(IOptions<UserTokenSetting> userTokenSetting, IOptions<MSGraphSetting> mSGraphSetting, ILogger logger, IEmailAccountManager emailAccountManager)
        {
            if (userTokenSetting is null)
            {
                throw new System.ArgumentNullException(nameof(userTokenSetting));
            }

            if (mSGraphSetting is null)
            {
                throw new System.ArgumentNullException(nameof(mSGraphSetting));
            }

            this.userTokenSetting = userTokenSetting.Value;
            this.mSGraphSetting = mSGraphSetting.Value;
            this.logger = logger ?? throw new System.ArgumentNullException(nameof(logger));
            this.emailAccountManager = emailAccountManager;
        }

        /// <summary>
        /// return the authentication header for selected account.
        /// </summary>
        /// <param name="selectedAccountCredential">selectedAccountCredential.</param>
        /// <returns>AuthenticationHeaderValue.</returns>
        public async Task<AuthenticationHeaderValue> GetAuthenticationHeaderValueForSelectedAccount(AccountCredential selectedAccountCredential)
        {
            AuthenticationHeaderValue authenticationHeaderValue = await this.GetAuthenticationHeaderFromToken(await this.GetAccessTokenForSelectedAccount(selectedAccountCredential).ConfigureAwait(false)).ConfigureAwait(false);
            return authenticationHeaderValue;
        }

        /// <inheritdoc/>
        public async Task<string> GetAccessTokenForSelectedAccount(AccountCredential selectedAccountCredential)
        {
            var traceProps = new Dictionary<string, string>();
            if (selectedAccountCredential == null)
            {
                throw new ArgumentNullException(nameof(selectedAccountCredential));
            }

            traceProps[AIConstants.EmailAccount] = selectedAccountCredential.AccountName;

            this.logger.TraceInformation($"Started {nameof(this.GetAccessTokenForSelectedAccount)} method of {nameof(TokenHelper)}.", traceProps);
            string authority = this.userTokenSetting.Authority;
            string clientId = this.userTokenSetting.ClientId;
            string userEmail = selectedAccountCredential?.AccountName;
            string userPassword = System.Web.HttpUtility.UrlEncode(selectedAccountCredential.PrimaryPassword);
            var token = string.Empty;
            using (HttpClient client = new HttpClient())
            {
                var tokenEndpoint = $"{authority}";
                var accept = ApplicationConstants.JsonMIMEType;

                client.DefaultRequestHeaders.Add("Accept", accept);
                string postBody = $"resource={clientId}&client_id={clientId}&grant_type=password&username={userEmail}&password={userPassword}&scope=openid";

                using (var response = await client.PostAsync(tokenEndpoint, new StringContent(postBody, Encoding.UTF8, "application/x-www-form-urlencoded")).ConfigureAwait(false))
                {
                    if (response.IsSuccessStatusCode)
                    {
                        var jsonresult = JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                        token = (string)jsonresult["access_token"];
                    }
                    else
                    {
                        var errorResponse = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        traceProps["Message"] = errorResponse;
                        this.logger.TraceInformation($"An error occurred while fetching token for {selectedAccountCredential.AccountName}. Details: {errorResponse}", traceProps);
                        this.logger.WriteCustomEvent("Unable to get Access Token", traceProps);
                        token = null;
                    }
                }
            }

            this.logger.TraceInformation($"Finished {nameof(this.GetAccessTokenForSelectedAccount)} method of {nameof(TokenHelper)}.", traceProps);
            return token;
        }

        /// <inheritdoc/>
        public async Task<AuthenticationHeaderValue> GetAuthenticationHeaderFromToken(string userAccessToken)
        {
            if (string.IsNullOrWhiteSpace(userAccessToken))
            {
                throw new ArgumentNullException(nameof(userAccessToken), "Token cannot be empty while fetching Authentication Header value.");
            }

            this.logger.TraceInformation($"Started {nameof(this.GetAuthenticationHeaderFromToken)} method of {nameof(TokenHelper)}.");
            if (userAccessToken.StartsWith($"{ApplicationConstants.BearerAuthenticationScheme.ToLower(CultureInfo.InvariantCulture)} ", System.StringComparison.InvariantCultureIgnoreCase))
            {
                userAccessToken = userAccessToken.Remove(0, 7);
            }

            var resourceToken = await this.GetResourceAccessTokenFromUserToken(userAccessToken).ConfigureAwait(false);
            var authHeader = new AuthenticationHeaderValue(ApplicationConstants.BearerAuthenticationScheme, resourceToken);
            this.logger.TraceInformation($"Finished {nameof(this.GetAuthenticationHeaderFromToken)} method of {nameof(TokenHelper)}.");
            return authHeader;
        }

        /// <summary>
        /// Get an authentication token for the graph resource from the user access token.
        /// </summary>
        /// <param name="userAccessToken">User access token.</param>
        /// <returns>Authentication token for the graph resource defined in the graph provider.</returns>
        private async Task<string> GetResourceAccessTokenFromUserToken(string userAccessToken)
        {
            this.logger.TraceInformation($"Started {nameof(this.GetResourceAccessTokenFromUserToken)} method of {nameof(TokenHelper)}.");

            // UserAssertion userAssertion = new UserAssertion(userAccessToken, this.mSGraphSetting.UserAssertionType);
            string clientId = this.mSGraphSetting.ClientId;
            string clientValue = this.mSGraphSetting.ClientCredential;
            string authority = string.Format(CultureInfo.InvariantCulture, this.mSGraphSetting.Authority, this.mSGraphSetting.TenantId);

            // AuthenticationContext authContext = new AuthenticationContext(authority);
            // ClientCredential clientCredential = new ClientCredential(clientId, clientValue);
            // var result = await authContext.AcquireTokenAsync(this.mSGraphSetting.GraphResourceId, clientCredential, userAssertion).ConfigureAwait(false);
            // this.logger.TraceInformation($"Finished {nameof(this.GetResourceAccessTokenFromUserToken)} method of {nameof(TokenHelper)}.");
            // return result?.AccessToken;
#if DEBUG
            var app = await GetTokenUsingSni(clientId, "27D6D3122675FCC4FE11E4977A540FC74169E1F1").ConfigureAwait(false);
#else
            var app = await GetTokenUsingFic(authority, clientId, clientId).ConfigureAwait(false);
#endif
            this.logger.TraceInformation($"Finished {nameof(this.GetResourceAccessTokenFromUserToken)} method of {nameof(TokenHelper)}.");
            var authResult = await app.AcquireTokenOnBehalfOf(new[] { this.mSGraphSetting.GraphResourceId + "/.default" }, new UserAssertion(userAccessToken.Replace(BearerText, string.Empty, StringComparison.InvariantCultureIgnoreCase))).ExecuteAsync().ConfigureAwait(false);
            return authResult.AccessToken;
        }


        private static async Task<IConfidentialClientApplication> GetTokenUsingSni(string clientId, string certificateThumbprint)
        {
            var certificate = await GetCertificate(certificateThumbprint).ConfigureAwait(false);

            IConfidentialClientApplication confidentialClientApplication = ConfidentialClientApplicationBuilder.Create(clientId)
                                                                            .WithAuthority(AzureCloudInstance.AzurePublic, "microsoft.onmicrosoft.com")
                                                                            .WithCertificate(certificate)
                                                                            .Build();

            return confidentialClientApplication;
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

        private static async Task<IConfidentialClientApplication> GetTokenUsingFic(string authority, string clientId, string resourceId)
        {
            IConfidentialClientApplication clientApplicationWithManagedIdentity = ConfidentialClientApplicationBuilder.Create(clientId).WithAuthority(new Uri(authority))
                   .WithClientAssertion((AssertionRequestOptions options) =>
                   {
                       DefaultAzureCredential defaultAzureCredential;

                       defaultAzureCredential = new DefaultAzureCredential();
                       var accessToken = defaultAzureCredential.GetToken(new TokenRequestContext(new string[] { resourceId }), CancellationToken.None);
                       return Task.FromResult(accessToken.Token);
                   }).Build();

            return clientApplicationWithManagedIdentity;
        }
    }
}
