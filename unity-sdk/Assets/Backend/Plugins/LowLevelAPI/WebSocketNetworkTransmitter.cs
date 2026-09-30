using System;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using WebSocketSharp;

public class WebSocketNetworkTransmitter : MonoBehaviour
{
    public bool connected;

    protected byte[] dataBuffer = new byte[1024];

    private WebSocket client;

    private void Awake()
    {

    }

    public byte Send(int host, int connection, int channel, byte[] data)
    {
        client.SendAsync(data, null);
        return 0;
    }

    protected byte DisconnectFromServer()
    {
        if (!connected || !client.IsAlive)
            return 0;
        client.CloseAsync();
        return 0;
    }

    protected byte ConnectToServer(string ip, int port)
    {
        client = new WebSocket(ip);
        client.OnOpen += (s, e) => ConnectEvent(0, 0);
        client.OnClose += (s, e) => DisconnectEvent(0, 0);
        client.OnError += (s, e) => LogNetworkError(e.Message);
        client.OnMessage += (s, e) =>
           {
               DataEvent(0, 0, 0, e.RawData.Length, e.RawData);
           };
        client.ConnectAsync();
        return 0;
    }

    void OnDestroy()
    {
        if (client != null)
            client.Close();
    }


    protected virtual void ConnectEvent(int hostId, int connectionId) { }
    protected virtual void DisconnectEvent(int hostId, int connectionId) { }
    protected virtual void DataEvent(int hostId, int connectionId, int channelId, int size, byte[] data) { }

    protected virtual void CheckForError(string label)
    {

    }

    private void LogNetworkError(string err)
    {
        //Debug.LogError("Error: " + err);
    }
}
