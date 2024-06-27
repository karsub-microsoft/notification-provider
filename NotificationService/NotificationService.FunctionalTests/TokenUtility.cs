// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace NotificationService.FunctionalTests
{
    using Microsoft.Extensions.Configuration;
    using System.Threading.Tasks;

    public class TokenUtility
    {
        private IConfiguration Configuration;

        public TokenUtility(IConfiguration configuration)
        {
            this.Configuration = configuration;
        }


        public async Task<string> GetTokenAsync()
        {
            return this.Configuration[FunctionalConstants.AuthToken];
        }
    }
}
