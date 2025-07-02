using Godot;
using Google.Protobuf;
using System;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Pkt = Client.Packets;

namespace Client
{
    internal partial class WebSocket : Node2D, IDisposable
    {
        private ClientWebSocket WS;
        private CancellationTokenSource CTS;

        [Signal]
        public delegate void OnConnectedEventHandler();
        [Signal]
        public delegate void OnConnectionClosedEventHandler();
        [Signal]
        public delegate void OnPacketReceivedEventHandler(string payload);

        public int ReceiveBufferSize { get; set; } = 1024;

        public async Task ConnectAsync(string url)
        {
            if (WS != null)
            {
                if (WS.State == WebSocketState.Open)
                {
                    return;
                }
                else
                {
                    WS.Dispose();
                }
            }

            WS = new();
            
            if (CTS != null)
            {
                CTS.Dispose();
            }

            CTS = new();

            await WS.ConnectAsync(new Uri(url), CTS.Token);
            await Task.Factory.StartNew(ReceiveLoop, CTS.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            EmitSignal("OnConnected");
        }

        public async Task DisconnectAsync()
        {
            if (WS is null)
            {
                return;
            }

            if (WS.State == WebSocketState.Open)
            {
                CTS.CancelAfter(TimeSpan.FromSeconds(2));
                await WS.CloseOutputAsync(WebSocketCloseStatus.Empty, "", CancellationToken.None);
                await WS.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
            }
            WS.Dispose();
            WS = null;
            CTS.Dispose();
            CTS = null;
            EmitSignal("OnConnectionClosed");
        }

        private async Task ReceiveLoop()
        {
            GD.Print("ReceiveLoop");
            var loopToken = CTS.Token;
            MemoryStream outputStream = null;
            WebSocketReceiveResult receiveResult = null;
            var buffer = new byte[ReceiveBufferSize];
            try
            {
                while (!loopToken.IsCancellationRequested)
                {
                    GD.Print("IsCancellationRequested " + loopToken.IsCancellationRequested);
                    outputStream = new(ReceiveBufferSize);
                    do
                    {
                        GD.Print("Receiving...");
                        receiveResult = await WS.ReceiveAsync(buffer, CTS.Token);
                        if (receiveResult.MessageType != WebSocketMessageType.Close)
                        {
                            outputStream.Write(buffer, 0, receiveResult.Count);
                        }
                    }
                    while (!receiveResult.EndOfMessage);
                    if (receiveResult.MessageType == WebSocketMessageType.Close)
                    {
                        GD.Print("MessageType " + receiveResult.MessageType);
                        break;
                    }
                    outputStream.Position = 0;
                    GetPacket(outputStream);
                }
            } catch (Exception e) {
                GD.Print("Error: " + e);
            } finally
            {
                GD.Print("outputStream disposed");
                outputStream?.Dispose();
            }
        }

        public async Task SendMessageAsync(Pkt.Packet packet)
        {
            packet.SenderId = 0;
            var data = packet.ToByteArray();
            await WS.SendAsync(data, WebSocketMessageType.Text, true, CTS.Token);
        }

        private void GetPacket(Stream inputStream)
        {
            GD.Print("Packet received");

            try
            {
                using var codedInput = new CodedInputStream(inputStream);
                var packet = new Pkt.Packet();
                codedInput.ReadMessage(packet);
                var rawBytes = packet.ToByteArray();
                var base64String = Convert.ToBase64String(rawBytes);
                //EmitSignal("OnPacketReceived", base64String);
                CallDeferred("emit_signal", "OnPacketReceived", base64String);
            }
            catch (Exception ex)
            {
                GD.PrintErr("Error on decoding packet: ", ex.Message);
            }

            //if (inputStream == null)
            //{
            //    return;
            //}

            //var bs = ByteString.FromStream(inputStream);
            //EmitSignal("OnPacketReceived", bs.ToBase64());
        }

        public void Dispose() => DisconnectAsync().Wait();
    }
}
