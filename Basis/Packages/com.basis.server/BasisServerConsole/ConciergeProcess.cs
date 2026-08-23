using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using Basis.Network.Core;

namespace BasisNetworkConsole
{
    /// <summary>Owns the colocated Go Concierge child process for standalone Basis servers.</summary>
    internal sealed class ConciergeProcess : IDisposable
    {
        private Process _process;
        private volatile bool _stopping;

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
            string adminUiDirectory = Path.Combine(conciergeDirectory, "adminui");
            if (Directory.Exists(adminUiDirectory))
                startInfo.Environment["ADMIN_UI_DIR"] = adminUiDirectory;
            startInfo.Environment["ASPNETCORE_URLS"] = string.IsNullOrWhiteSpace(configuration.SsoBrokerBindUrl)
                ? "http://127.0.0.1:5080"
                : configuration.SsoBrokerBindUrl;
            _process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the colocated Concierge process.");
            _stopping = false;
            _process.EnableRaisingEvents = true;
            _process.Exited += (_, _) =>
            {
                if (!_stopping)
                    BNL.LogWarning($"Colocated Concierge exited unexpectedly (pid {_process?.Id}). SSO admission is unavailable.");
            };
            try
            {
                WaitForListening(startInfo.Environment["ASPNETCORE_URLS"]);
            }
            catch
            {
                Dispose();
                throw;
            }
            BNL.Log($"Started Go Concierge (pid {_process.Id}) at {startInfo.Environment["ASPNETCORE_URLS"]}.");
        }

        private void WaitForListening(string bindUrl)
        {
            if (!Uri.TryCreate(bindUrl, UriKind.Absolute, out Uri? uri) || uri.Port <= 0)
                throw new InvalidOperationException($"Concierge bind URL is invalid: {bindUrl}");
            // ASP.NET accepts wildcard bind hosts (0.0.0.0, ::, +, *), but
            // those are not routable probe destinations. Probe loopback while
            // preserving the address family selected by the configured URL.
            string probeHost = uri.Host switch
            {
                "0.0.0.0" or "+" or "*" => "127.0.0.1",
                "::" or "[::]" => "::1",
                _ => uri.Host,
            };
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    bool ipv6 = uri.HostNameType == UriHostNameType.IPv6 || probeHost == "::1";
                    using var socket = new TcpClient(ipv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork);
                    socket.Connect(probeHost, uri.Port);
                    return;
                }
                catch (SocketException) when (DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(100);
                }
            }
            throw new InvalidOperationException($"Colocated Concierge did not listen on {bindUrl} within 10 seconds.");
        }

        public void Dispose()
        {
            Process process = _process;
            _process = null;
            if (process == null) return;
            _stopping = true;
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
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
