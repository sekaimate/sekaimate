using System;
using System.Diagnostics;
using System.IO;
using Basis.Network.Core;

namespace BasisNetworkConsole
{
    /// <summary>Owns the colocated SSO broker child process for the standalone Basis server.</summary>
    internal sealed class BasisSsoBrokerProcess : IDisposable
    {
        private Process _process;

        public void Start(Configuration configuration, string serverBaseDirectory)
        {
            if (configuration == null || !configuration.RequireSso || !configuration.AutoStartSsoBroker) return;
            if (string.IsNullOrWhiteSpace(configuration.SsoAdmissionTicketSigningKey))
                throw new InvalidOperationException("SSO is required but no admission-ticket signing key is configured.");

            string configuredDirectory = configuration.SsoBrokerDirectory?.Trim();
            string brokerDirectory = Path.IsPathRooted(configuredDirectory)
                ? configuredDirectory
                : Path.Combine(serverBaseDirectory, string.IsNullOrWhiteSpace(configuredDirectory) ? "sso-broker" : configuredDirectory);
            string brokerAssembly = Path.Combine(brokerDirectory, "BasisSsoBroker.dll");
            string brokerSettings = Path.Combine(brokerDirectory, "appsettings.json");
            if (!File.Exists(brokerAssembly) || !File.Exists(brokerSettings))
            {
                throw new FileNotFoundException(
                    "SSO is required but the colocated broker is not installed. Run Tools/BasisSsoBroker/publish-for-basis-server.sh <server-directory>, configure sso-broker/appsettings.json, then restart.",
                    !File.Exists(brokerAssembly) ? brokerAssembly : brokerSettings);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = Quote(brokerAssembly),
                WorkingDirectory = brokerDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.Environment["BASIS_SSO_TICKET_SIGNING_KEY"] = configuration.SsoAdmissionTicketSigningKey;
            startInfo.Environment["ASPNETCORE_URLS"] = string.IsNullOrWhiteSpace(configuration.SsoBrokerBindUrl)
                ? "http://127.0.0.1:5080"
                : configuration.SsoBrokerBindUrl;
            _process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the colocated SSO broker process.");
            BNL.Log($"Started SSO admission broker (pid {_process.Id}) at {startInfo.Environment["ASPNETCORE_URLS"]}.");
        }

        public void Dispose()
        {
            Process process = _process;
            _process = null;
            if (process == null) return;
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
            }
            catch (Exception exception)
            {
                BNL.LogWarning($"Could not stop the SSO broker cleanly: {exception.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }

        private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
    }
}
