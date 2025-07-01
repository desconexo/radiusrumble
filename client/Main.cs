using Godot;
using Client.Packets;
using Client;
using Google.Protobuf;
using System.Buffers.Text;
using System;

public partial class Main : Control
{

    private GameManager gameManager;

    public override void _Ready()
    {
        gameManager = GetNode<GameManager>("/root/GameManager");
        gameManager.SetState(GameManager.State.ENTERED);
    }
}
