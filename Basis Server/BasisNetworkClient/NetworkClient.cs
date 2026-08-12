using Basis.Network.Core;
using Basis.Network.WebSocketClient;
using System;

using static Basis.Network.Core.Serializable.SerializableBasis;
using static SerializableBasis;
public class NetworkClient
{
    public  NetManager client;
    public EventBasedNetListener listener;
    private NetPeer peer;
    private bool IsInUse;
    /// <summary>
    /// initial data is typically the 
    /// </summary> 
    /// <param name="IP"></param>
    /// <param name="port"></param>
    /// <param name="ReadyMessage"></param>
    public NetPeer StartClient(string IP, int port, ReadyMessage ReadyMessage, byte[] AuthenticationMessage, Configuration Configuration, bool manualMode = false)
    {
        return StartClient(IP, port, ReadyMessage, AuthenticationMessage, Configuration, null, manualMode);
    }
    public NetPeer StartClient(
        string IP,
        int port,
        ReadyMessage ReadyMessage,
        byte[] AuthenticationMessage,
        Configuration Configuration,
        Action<EventBasedNetListener> configureListener,
        bool manualMode = false)
    {
        if (IsInUse == false)
        {
            listener = new EventBasedNetListener();
            configureListener?.Invoke(listener);
            Configuration.NetworkStackId = NetworkStackSelection.ResolveClientStackId(Configuration.NetworkStackId);
#if UNITY_WEBGL && !UNITY_EDITOR
            RegisterWebSocketStack();
#endif
            client = BasisNetworkStackRegistry.Create(Configuration.NetworkStackId, listener, Configuration);
            if (manualMode)
                client.StartManual();
            else
                client.Start();
            NetDataWriter Writer = new NetDataWriter(true,12);
            //this is the only time we dont put key!
            Writer.Put(BasisNetworkVersion.ServerVersion);
            BytesMessage AuthBytes = new BytesMessage();
            AuthBytes.Serialize(Writer, AuthenticationMessage);
            ReadyMessage.Serialize(Writer);
            peer = client.Connect(IP, port, Writer);
            IsInUse = true;
            return peer;
        }
        else
        {
            BNL.LogError("Call Shutdown First!");
            return null;
        }
    }
#if UNITY_WEBGL && !UNITY_EDITOR
    private static void RegisterWebSocketStack()
    {
        if (BasisNetworkStackRegistry.IsRegistered(BasisNetworkStackRegistry.WebSocketId)) return;
        BasisNetworkStackRegistry.Register(
            BasisNetworkStackRegistry.WebSocketId,
            "WebSocket",
            (listener, configuration) => new WebSocketNetManager(
                listener,
                configuration,
                new WebSocketBrowserBridge(),
                1024 * 1024,
                256));
        BasisNetworkStackRegistry.RegisterParser(
            BasisNetworkStackRegistry.WebSocketId,
            new WebSocketConnectionTargetParser());
    }
#endif
    public void Poll()
    {
        client?.PollEvents();
    }
    public void Update(float elapsedMilliseconds)
    {
        client?.ManualUpdate(elapsedMilliseconds);
    }
    public void Disconnect()
    {
        IsInUse = false;
        BNL.Log("Client Called Disconnect from server");
        peer?.Disconnect();
        client?.Stop();

        BNL.Log("Worker thread stopped.");
    }
}
