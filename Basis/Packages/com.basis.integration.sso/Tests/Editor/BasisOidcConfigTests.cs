using NUnit.Framework;

namespace Basis.Integration.Sso.Tests
{
    public sealed class BasisOidcConfigTests
    {
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
