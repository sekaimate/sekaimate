using System;
using System.Diagnostics;
using System.IO;
using Basis.Network.Core;

namespace BasisNetworkConsole
{
    /// <summary>Owns the colocated Go Concierge child process for standalone Basis servers.</summary>
    internal sealed class ConciergeProcess : IDisposable
    {
        private Process _process;

        public void Start(Configuration configuration, string serverBaseDirectory)
        {
            if (configuration == null || !configuration.RequireSso || !configuration.AutoStartSsoBroker) return;
            if (string.IsNullOrWhiteSpace(configuration.SsoAdmissionTicketSigningKey))
                throw new InvalidOperationException("SSO is required but no admission-ticket signing key is configured.");

            string? configuredDirectory = configuration.SsoBrokerDirectory?.Trim();
            string configuredPath = Path.IsPathRooted(configuredDirectory)
                ? configuredDirectory
                : Path.Combine(serverBaseDirectory, string.IsNullOrWhiteSpace(configuredDirectory) ? "concierge" : configuredDirectory);
            string executableName = OperatingSystem.IsWindows() ? "concierge.exe" : "concierge";
            string? conciergeDirectory = null;
            foreach (string candidate in new[] { configuredPath, Path.Combine(serverBaseDirectory, "concierge"), Path.Combine(serverBaseDirectory, "sso-broker") }.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (File.Exists(Path.Combine(candidate, executableName)) && File.Exists(Path.Combine(candidate, "appsettings.json")))
                {
                    conciergeDirectory = candidate;
                    break;
                }
            }
            if (conciergeDirectory == null)
            {
                string expected = Path.Combine(configuredPath, executableName);
                throw new FileNotFoundException(
                    "SSO is required but the colocated Concierge is not installed. Run concierge/publish-for-basis-server.sh <server-directory>, configure concierge/appsettings.json, then restart.",
                    expected);
            }
            string executablePath = Path.Combine(conciergeDirectory, executableName);
            string settingsPath = Path.Combine(conciergeDirectory, "appsettings.json");

            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = conciergeDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.Environment["BASIS_SSO_BROKER_CONFIG_PATH"] = settingsPath;
            startInfo.Environment["BASIS_SSO_TICKET_SIGNING_KEY"] = configuration.SsoAdmissionTicketSigningKey;
            startInfo.Environment["BASIS_SSO_TRANSPORT_PUBLIC_KEY"] = configuration.SsoTransportPublicKey ?? string.Empty;
            startInfo.Environment["ASPNETCORE_URLS"] = string.IsNullOrWhiteSpace(configuration.SsoBrokerBindUrl)
                ? "http://127.0.0.1:5080"
                : configuration.SsoBrokerBindUrl;
            _process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the colocated Concierge process.");
            BNL.Log($"Started Go Concierge (pid {_process.Id}) at {startInfo.Environment["ASPNETCORE_URLS"]}.");
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
                BNL.LogWarning($"Could not stop Concierge cleanly: {exception.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }
    }
}
