using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace Basis.Integration.Sso.Tests
{
    public sealed class BasisOidcConfigTests
    {
        [Test]
        public void EmptyStreamingPlaceholder_IsRecognizedWithoutIssuer()
        {
            Assert.That(BasisOidcConfig.IsEmptyPlaceholder(
                "{\"_comment\":\"runtime broker configuration\"}"), Is.True);
        }

        [Test]
        public void BrowserRedirectAcceptsProviderHttpsTokenEndpoint()
        {
            BasisOidcConfig config = BrowserConfig("https://broker.example/web-oidc/google/token");

            Assert.That(config.TryValidate(out string error), Is.True, error);
        }

        [Test]
        public void BrowserRedirectRejectsProviderRemoteHttpTokenEndpoint()
        {
            BasisOidcConfig config = BrowserConfig("http://broker.example/web-oidc/google/token");

            Assert.That(config.TryValidate(out string error), Is.False);
            Assert.That(error, Is.EqualTo("OIDC config: browser redirects require a safe path and HTTPS tokenEndpoint."));
        }

        [Test]
        public void BrowserRedirectAcceptsLoopbackHttpAdmissionEndpoint()
        {
            BasisOidcConfig config = BrowserConfig("http://127.0.0.1:15080/web-oidc/google/token");
            config.ServerTransport.AdmissionEndpoint = "http://127.0.0.1:15080/admission/local";
            config.ServerTransport.AllowUntrustedLoopbackCertificate = true;

            Assert.That(config.TryValidate(out string error), Is.True, error);
        }

        [Test]
        public void BrowserRedirectRejectsRemoteHttpAdmissionEndpoint()
        {
            BasisOidcConfig config = BrowserConfig("https://broker.example/web-oidc/google/token");
            config.ServerTransport.AdmissionEndpoint = "http://broker.example/admission/local";

            Assert.That(config.TryValidate(out string error), Is.False);
            Assert.That(error, Is.EqualTo(
                "OIDC config: serverTransport.admissionEndpoint must use HTTPS; HTTP is allowed only for localhost or a loopback IP."));
        }

        [Test]
        public void OrganizationWildcardAcceptsNonEmptyHostedDomainClaim()
        {
            BasisOidcConfig config = BrowserConfig("https://broker.example/web-oidc/google/token");
            config.Access.AllowedClaims.Add(new BasisOidcConfig.ClaimRule
            {
                Claim = "hd",
                Values = new List<string> { "*" },
            });
            var session = new BasisSsoSession
            {
                Claims = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["hd"] = new List<string> { "example.org" },
                },
            };

            Assert.That(BasisSsoAccessControl.Evaluate(config, session).Allowed, Is.True);
        }

        [Test]
        public void OrganizationWildcardRejectsMissingHostedDomainClaim()
        {
            BasisOidcConfig config = BrowserConfig("https://broker.example/web-oidc/google/token");
            config.Access.AllowedClaims.Add(new BasisOidcConfig.ClaimRule
            {
                Claim = "hd",
                Values = new List<string> { "*" },
            });

            Assert.That(BasisSsoAccessControl.Evaluate(config, new BasisSsoSession()).Allowed, Is.False);
        }

        private static BasisOidcConfig BrowserConfig(string tokenEndpoint)
        {
            BasisOidcConfig config = new BasisOidcConfig
            {
                DefaultProviderId = "google",
                Redirect = new BasisOidcConfig.RedirectConfig
                {
                    Mode = "browser",
                    Path = "/sso-callback",
                },
                ServerTransport = new BasisOidcConfig.ServerTransportConfig
                {
                    ServerPublicKey = "public-key",
                    AdmissionEndpoint = "https://broker.example/admission/local",
                },
            };
            config.Providers.Add(new BasisOidcConfig.ProviderConfig
            {
                Id = "google",
                Issuer = "https://accounts.google.com",
                ClientId = "client-id",
                TokenEndpoint = tokenEndpoint,
            });
            return config;
        }
    }
}
