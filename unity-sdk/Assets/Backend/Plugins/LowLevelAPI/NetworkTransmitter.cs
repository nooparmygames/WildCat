using System;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class NetworkTransmitter : MonoBehaviour
{
    protected int clientId;
    public bool connected;

    protected byte error;
    protected int hostId, connectionId;
    protected int channelId, receivedSize;
    protected byte[] dataBuffer = new byte[1024];

    private const int maxClients = 5;
    private NetworkEventType netEvent;
    
    private void Awake()
    {
        NetworkTransport.Init();
        var topology = new HostTopology(MakeConfig(), maxClients);
        clientId = NetworkTransport.AddHost(topology, 0);
    }

    private static ConnectionConfig MakeConfig()
    {
        var config = new ConnectionConfig { PacketSize = 1440 };
        config.NetworkDropThreshold = 95;
        config.OverflowDropThreshold = 50;
        config.PingTimeout = 1000;
        config.DisconnectTimeout *= 2;

        config.AddChannel(QosType.Reliable);
        config.AddChannel(QosType.ReliableFragmented);
        config.AddChannel(QosType.Unreliable);

        return config;
    }

    public byte Send(int host, int connection, int channel, byte[] data)
    {
        NetworkTransport.Send(host, connection, channel, data, data.Length, out error);
        CheckForError("Send: ");
        return error;
    }

    protected byte DisconnectFromServer()
    {
        if (!connected || !NetworkTransport.IsStarted)
            return 0;
        byte error;
        NetworkTransport.Disconnect(hostId, connectionId,out error);
        return error;
    }

    protected byte ConnectToServer(string ip, int port)
    {
        NetworkTransport.Connect(clientId, ip, port, 0, out error);
        CheckForError("Connect: ");
        return error;
    }

    void OnDestroy()
    {
        NetworkTransport.Shutdown();
    }
    
    void Update()
    {
        netEvent = NetworkTransport.Receive(
                out hostId, out connectionId, out channelId,
                dataBuffer, dataBuffer.Length, out receivedSize, out error);

        CheckForError("Receive: ");
        FigureNetworkEventOut();
    }

    private void FigureNetworkEventOut()
    {
        switch (netEvent)
        {
            case NetworkEventType.ConnectEvent:
                {
                    ConnectEvent(hostId,connectionId);
                    break;
                }
            case NetworkEventType.DisconnectEvent:
                {
                    DisconnectEvent(hostId,connectionId);
                    break;
                }
            case NetworkEventType.DataEvent:
                {
                    DataEvent(hostId,connectionId,channelId,receivedSize,dataBuffer);
                    break;
                }
        }
    }

    protected virtual void ConnectEvent(int hostId,int connectionId) { }
    protected virtual void DisconnectEvent(int hostId,int connectionId) { }
    protected virtual void DataEvent(int hostId,int connectionId,int channelId,int size,byte[] data) { }

    protected virtual void CheckForError(string label)
    {
        if (error != 0)
            LogNetworkError(label);
    }

    private void LogNetworkError(string label)
    {
        Debug.LogError(label + " Error code: " + (NetworkError)error);
    }
}
