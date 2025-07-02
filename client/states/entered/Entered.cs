using Client;
using Client.Packets;
using Godot;
using System;

public partial class Entered : Node
{
    private GameManager gameManager;
    private WebSocket WS;
    private Log log;

    public override void _Ready()
    {
        log = GetNode<Log>("UI/Log");

        gameManager = GetNode<GameManager>("/root/GameManager");
        WS = GetNode<WebSocket>("/root/WS");

        WS.Connect("OnConnected", Callable.From(onWebSocketConnected));
        WS.Connect("OnConnectionClosed", Callable.From(onWebSocketConnectionClosed));
        WS.Connect("OnPacketReceived", Callable.From<string>(onWebSocketPacketReceived));

        log.Info("Connecting to server...");
        WS.ConnectAsync("wss://gameserver-251090453108.southamerica-east1.run.app/ws");
    }

    private void onWebSocketConnected()
    {
        log.Success("Connected successfully");
    }

    private void onWebSocketConnectionClosed()
    {
        log.Warning("Connection closed");
    }

    private void onWebSocketPacketReceived(string payload)
    {

        var bytes = Convert.FromBase64String(payload);
        var packet = Packet.Parser.ParseFrom(bytes);

        if (packet.Id != null)
        {
            _handleIdMessage(packet.SenderId, packet.Id);
        }
    }

    private void _handleIdMessage(ulong senderId, IdMessage id)
    {
        gameManager.ClientId = id.Id;
        gameManager.SetState(GameManager.State.CONNECTED);
        log.Info($"Received client ID: {id.Id}");
    }

}
