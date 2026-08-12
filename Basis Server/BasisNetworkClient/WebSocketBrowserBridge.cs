#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using AOT;

namespace Basis.Network.WebSocketClient
{
    public sealed class WebSocketBrowserBridge : IWebSocketBrowserBridge
    {
        public IWebSocketBrowserConnection Open(string absoluteUri, IWebSocketBrowserEventSink sink)
        {
            return BrowserConnection.Open(absoluteUri, sink);
        }

        private sealed class BrowserConnection : IWebSocketBrowserConnection
        {
            private static readonly Dictionary<int, BrowserConnection> Connections = new Dictionary<int, BrowserConnection>();
            private static int _nextConnectionId;

            private readonly int _connectionId;
            private readonly IWebSocketBrowserEventSink _sink;
            private bool _closeRequested;

            private BrowserConnection(int connectionId, IWebSocketBrowserEventSink sink)
            {
                _connectionId = connectionId;
                _sink = sink;
            }

            public static BrowserConnection Open(string absoluteUri, IWebSocketBrowserEventSink sink)
            {
                if (string.IsNullOrEmpty(absoluteUri)) throw new ArgumentException("A WebSocket URI is required.", nameof(absoluteUri));
                if (sink == null) throw new ArgumentNullException(nameof(sink));

                int connectionId = ++_nextConnectionId;
                BrowserConnection connection = new BrowserConnection(connectionId, sink);
                Connections.Add(connectionId, connection);
                BasisWebSocketOpen(
                    connectionId,
                    absoluteUri,
                    HandleOpen,
                    HandleMessage,
                    HandleError,
                    HandleClose);
                return connection;
            }

            public bool Send(byte[] payload)
            {
                if (_closeRequested || payload == null)
                {
                    return false;
                }
                return BasisWebSocketSend(_connectionId, payload, payload.Length) == 1;
            }

            public void Close(ushort code, string reason)
            {
                if (_closeRequested)
                {
                    return;
                }
                _closeRequested = true;
                BasisWebSocketClose(_connectionId, code, reason ?? string.Empty);
            }

            [MonoPInvokeCallback(typeof(OpenCallback))]
            private static void HandleOpen(int connectionId)
            {
                if (Connections.TryGetValue(connectionId, out BrowserConnection connection))
                {
                    connection._sink.OnBrowserOpen();
                }
            }

            [MonoPInvokeCallback(typeof(MessageCallback))]
            private static void HandleMessage(int connectionId, IntPtr payloadPointer, int payloadLength)
            {
                if (!Connections.TryGetValue(connectionId, out BrowserConnection connection) || payloadLength < 0)
                {
                    return;
                }
                byte[] payload = new byte[payloadLength];
                if (payloadLength > 0)
                {
                    Marshal.Copy(payloadPointer, payload, 0, payloadLength);
                }
                connection._sink.OnBrowserMessage(payload);
            }

            [MonoPInvokeCallback(typeof(ErrorCallback))]
            private static void HandleError(int connectionId)
            {
                if (Connections.TryGetValue(connectionId, out BrowserConnection connection))
                {
                    connection._sink.OnBrowserError("Browser WebSocket error.");
                }
            }

            [MonoPInvokeCallback(typeof(CloseCallback))]
            private static void HandleClose(int connectionId, int code, IntPtr reasonPointer, int reasonLength)
            {
                if (!Connections.Remove(connectionId, out BrowserConnection connection))
                {
                    return;
                }
                string reason = DecodeUtf8(reasonPointer, reasonLength);
                ushort closeCode = code >= 0 && code <= ushort.MaxValue ? (ushort)code : (ushort)1006;
                connection._sink.OnBrowserClose(closeCode, reason);
            }

            private static string DecodeUtf8(IntPtr pointer, int length)
            {
                if (pointer == IntPtr.Zero || length <= 0)
                {
                    return string.Empty;
                }
                byte[] bytes = new byte[length];
                Marshal.Copy(pointer, bytes, 0, length);
                return Encoding.UTF8.GetString(bytes);
            }

            private delegate void OpenCallback(int connectionId);
            private delegate void MessageCallback(int connectionId, IntPtr payloadPointer, int payloadLength);
            private delegate void ErrorCallback(int connectionId);
            private delegate void CloseCallback(int connectionId, int code, IntPtr reasonPointer, int reasonLength);

            [DllImport("__Internal")]
            private static extern void BasisWebSocketOpen(
                int connectionId,
                string absoluteUri,
                OpenCallback onOpen,
                MessageCallback onMessage,
                ErrorCallback onError,
                CloseCallback onClose);

            [DllImport("__Internal")]
            private static extern int BasisWebSocketSend(int connectionId, byte[] payload, int payloadLength);

            [DllImport("__Internal")]
            private static extern void BasisWebSocketClose(int connectionId, int code, string reason);
        }
    }
}
#endif
