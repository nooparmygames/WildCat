using Microsoft.AspNetCore.Http;
using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace WildCat.Gateway
{
    /// <summary>
    /// Processes our websocket connections and sends and receives data for them.
    /// This is only the low level processing and parsing of messages and executing logic happens in the FrontEnd class
    /// </summary>
    public class ConnectionProcessor
    {
        public event Func<byte[], int, Task> OnDataReceived;
        public event Func<ConnectionProcessor, int, Task> OnConnected;
        public event Func<ConnectionProcessor, int, Task> OnDisConnected;

        private SemaphoreSlim sendLock = new SemaphoreSlim(1);

        public int ID { get; private set; }
        private static int lastId = int.MinValue;
        private static object idLock = new object();
        public WebSocket socket { get; private set; }

        public static int GetNextId()
        {
            lock (idLock)
            {
                return ++lastId;
            }
        }

        public ConnectionProcessor(WebSocket socket, int id)
        {
            this.socket = socket;
            this.ID = id;
        }

        public async Task Process(HttpContext context, WebSocket webSocket)
        {
            await (OnConnected?.Invoke(this, ID) ?? Task.CompletedTask);
            await ReceiveMessages(webSocket);
            await (OnDisConnected?.Invoke(this, ID) ?? Task.CompletedTask);
        }

        private async Task ReceiveMessages(WebSocket webSocket)
        {
            byte[] receiveBuffer = new byte[1024 * 4];
            byte[] messageBuffer = new byte[1024 * 2048];
            bool isProcessingLargeMessage = false;
            int offset = 0;
            // While the WebSocket connection remains open run a simple loop that receives data and sends it back.
            while (webSocket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult receiveResult = await webSocket.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), CancellationToken.None);
                if (receiveResult.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                }
                else if (receiveResult.MessageType == WebSocketMessageType.Binary)
                {
                    ArraySegment<byte> segment = new ArraySegment<byte>(receiveBuffer, 0, receiveResult.Count);
                    var dataToCOpy = segment.ToArray();
                    if (receiveResult.EndOfMessage && !isProcessingLargeMessage)
                    {
                        OnDataReceived?.Invoke(dataToCOpy, ID);
                    }
                    else
                    {
                        if (!isProcessingLargeMessage)
                        {
                            isProcessingLargeMessage = true;
                            offset = 0;
                        }
                        Buffer.BlockCopy(receiveBuffer, 0, messageBuffer, offset, receiveResult.Count);
                        offset += receiveResult.Count;
                        if (receiveResult.EndOfMessage)
                        {
                            segment = new ArraySegment<byte>(messageBuffer, 0, offset);
                            dataToCOpy = segment.ToArray();
                            OnDataReceived?.Invoke(dataToCOpy, ID);
                            isProcessingLargeMessage = false;
                        }
                    }
                }
                else
                {

                }
            }
        }

        public async Task<bool> Send(byte[] message)
        {
            bool success = false;
            await sendLock.WaitAsync();
            try
            {
                await socket.SendAsync(message, WebSocketMessageType.Binary, true, CancellationToken.None);
                success = true;
            }
            catch (Exception s)
            {
                Console.WriteLine(s.Message);
            }
            finally
            {
                sendLock.Release();
            }
            return success;
        }
    }
}
