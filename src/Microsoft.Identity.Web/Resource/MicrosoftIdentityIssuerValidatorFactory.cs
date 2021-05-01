// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web.InstanceDiscovery;
using Microsoft.IdentityModel.Protocols;

namespace Microsoft.Identity.Web.Resource
{
    /// <summary>
    /// Factory class for creating the IssuerValidator per authority.
    /// </summary>
    public class MicrosoftIdentityIssuerValidatorFactory
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MicrosoftIdentityIssuerValidatorFactory"/> class.
        /// </summary>
        /// <param name="aadIssuerValidatorOptions">Options passed-in to create the AadIssuerValidator object.</param>
        /// <param name="httpClientFactory">HttpClientFactory.</param>
        public MicrosoftIdentityIssuerValidatorFactory(
            IOptions<AadIssuerValidatorOptions> aadIssuerValidatorOptions,
            IHttpClientFactory httpClientFactory)
        {
            if (aadIssuerValidatorOptions?.Value?.HttpClientName != null && httpClientFactory != null)
            {
                _httpClient = httpClientFactory.CreateClient(aadIssuerValidatorOptions.Value.HttpClientName);
            }
        }

        private readonly IDictionary<string, AadIssuerValidator> _issuerValidators = new ConcurrentDictionary<string, AadIssuerValidator>();

        private HttpClient _httpClient;

        /// <summary>
        /// Gets an <see cref="AadIssuerValidator"/> for an authority.
        /// </summary>
        /// <param name="aadAuthority">The authority to create the validator for, e.g. https://login.microsoftonline.com/. </param>
        /// <returns>A <see cref="AadIssuerValidator"/> for the aadAuthority.</returns>
        /// <exception cref="ArgumentNullException">if <paramref name="aadAuthority"/> is null or empty.</exception>
        public AadIssuerValidator GetAadIssuerValidator(string aadAuthority)
        {
            if (string.IsNullOrEmpty(aadAuthority))
            {
                throw new ArgumentNullException(nameof(aadAuthority));
            }

            Uri.TryCreate(aadAuthority, UriKind.Absolute, out Uri? authorityUri);
            string authorityHost = authorityUri?.Authority ?? new Uri(Constants.FallbackAuthority).Authority;

            if (_issuerValidators.TryGetValue(authorityHost, out AadIssuerValidator? aadIssuerValidator))
            {
                return aadIssuerValidator;
            }

            ConfigurationManager<IssuerMetadata> configManager = CreateConfigurationManager(authorityHost);

            // In the constructor, we hit the Azure AD issuer metadata endpoint. The data is cached for 24 hrs.
            IssuerMetadata issuerMetadata = configManager.GetConfigurationAsync().ConfigureAwait(false).GetAwaiter().GetResult();

            Uri issuerEndpoint = new Uri(issuerMetadata.Issuer);
            // Add issuer aliases of the chosen authority to the cache
            _issuerValidators[authorityHost] = new AadIssuerValidator(new string[] { issuerEndpoint.Authority });

            return _issuerValidators[authorityHost];
        }

        private ConfigurationManager<IssuerMetadata> CreateConfigurationManager(string authorityHost)
        {
            // https://login.microsoftonline.com/common/.well-known/openid-configuration
            string metadataEndpoint = $"https://{authorityHost}/common/.well-known/openid-configuration";
            if (_httpClient != null)
            {
                return new ConfigurationManager<IssuerMetadata>(
                    metadataEndpoint,
                    new IssuerConfigurationRetriever(),
                    _httpClient);
            }

            return new ConfigurationManager<IssuerMetadata>(
                                               metadataEndpoint,
                                               new IssuerConfigurationRetriever());
        }
    }
}
