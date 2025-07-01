using Client;
using Client.Packets;
using Godot;
using Godot.Collections;
using System;

public partial class Ingame : Node
{
    private GameManager gameManager;
    private WebSocket WS;
    private Log log;
    private LineEdit lineEdit;
    private Button logoutButton;
    private Button sendButton;
    private Hiscores hiscores;
    private Actor actor;
    private Spore spore;
    private Node2D world;

    private Dictionary<int, Actor> players = new();
    private Dictionary<int, Spore> spores = new();

    public override void _Ready()
    {
        world = GetNode<Node2D>("World");
        log = GetNode<Log>("UI//MarginContainer/VBoxContainer/Log");
        lineEdit = GetNode<LineEdit>("UI//MarginContainer/VBoxContainer/HBoxContainer/LineEdit");
        logoutButton = GetNode<Button>("UI//MarginContainer/VBoxContainer/HBoxContainer/LogoutButton");
        sendButton = GetNode<Button>("UI//MarginContainer/VBoxContainer/HBoxContainer/SendButton");
        hiscores = GetNode<Hiscores>("UI/MarginContainer/VBoxContainer/Hiscores");
        WS = GetNode<WebSocket>("/root/WS");
        gameManager = GetNode<GameManager>("/root/GameManager");

        lineEdit.Connect("text_submitted", Callable.From<string>(onLineEditTextSubmitted));
        sendButton.Connect("pressed", Callable.From(onSendButtonPressed));
        logoutButton.Connect("pressed", Callable.From(onLogoutButtonPressed));
        WS.Connect("OnConnectionClosed", Callable.From(onWebSocketConnectionClosed));
        WS.Connect("OnPacketReceived", Callable.From<string>(onWebSocketPacketReceived));
    }

    private void onLogoutButtonPressed()
    {
        var packet = new Packet
        {
            Disconnect = new DisconnectMessage {
                Reason = "they logged out"
            }
        };

        WS.SendMessageAsync(packet).Wait();
        gameManager.SetState(GameManager.State.CONNECTED);
    }

    private void onSendButtonPressed()
    {
        onLineEditTextSubmitted(lineEdit.Text);
    }

    private void onWebSocketConnectionClosed()
    {
        log.Warning("Connection closed");
    }

    private void onWebSocketPacketReceived(string payload)
    {
        var bytes = Convert.FromBase64String(payload);
        var packet = Packet.Parser.ParseFrom(bytes);

        if (packet.Chat != null)
        {
            _handleChatMessage(packet.SenderId, packet.Chat);
        }
        else if (packet.Player != null)
        {
            _handlePlayerMessage(packet.SenderId, packet.Player);
        }
        else if (packet.Spore != null)
        {
            _handleSporeMessage(packet.SenderId, packet.Spore);
        }
        else if (packet.SporeBatch != null)
        {
            _handleSporeBatchMessage(packet.SenderId, packet.SporeBatch);
        }
        else if (packet.SporeConsumed != null)
        {
            _handleSporeConsumedMessage(packet.SenderId, packet.SporeConsumed);
        }
        else if (packet.Disconnect != null)
        {
            _handleDisconnectMessage(packet.SenderId, packet.Disconnect);
        }
    }

    private void _handlePlayerMessage(ulong senderId, PlayerMessage player)
    {
        var actorId = player.Id;
        var actorName = player.Name;
        var x = player.X;
        var y = player.Y;
        var radius = player.Radius;
        var speed = player.Speed;
        var color = new Color(player.Color);

        var isPlayer = actorId == gameManager.ClientId;

        if (!players.ContainsKey((int)actorId))
        {
            _addActor((int)actorId, actorName, (float)x, (float)y, (float)radius, (float)speed, isPlayer, color);
        }
        else
        {
            _updateActor((int)actorId, (float)x, (float)y, ((float)radius), ((float)player.Direction), (float)speed, isPlayer);
        }
    }

    private void _addActor(int actorId, string actorName, float x, float y, float radius, float speed, bool isPlayer, Color color)
    {
        var actor = Actor.Instantiate(
            actorId,
            actorName,
            x,
            y,
            radius,
            speed,
            isPlayer,
            color
        );

        world.AddChild(actor);
        actor.ZIndex = 1;
        _setActorMass(actor, _radToMass(radius));
        players[(int)actorId] = actor;

        if (isPlayer)
            actor.Connect("area_entered", Callable.From<Area2D>(_onPlayerAreaEntered));
    }

    private void _updateActor(int actorId, float x, float y, float radius, float direction, float speed, bool isPlayer)
    {
        var actor = players[(int)actorId];

        _setActorMass(actor, _radToMass(radius));

        if (actor.Position.DistanceSquaredTo(new Vector2(x, y)) > 100)
        {
            actor.ServerPosition = new Vector2(x, y);
        }

        if (!isPlayer)
        {
            actor.Velocity = speed * Vector2.FromAngle(direction);
        }
    }

    private void _handleChatMessage(ulong senderId, ChatMessage chat)
    {
        if (players.ContainsKey((int)senderId))
        {
            var actor = players[(int)senderId];
            log.Chat(actor.ActorName, chat.Msg);
        }
    }

    private void _handleSporeMessage(ulong senderId, SporeMessage sporeMessage)
    {
        var sporeId = sporeMessage.Id;
        var x = sporeMessage.X;
        var y = sporeMessage.Y;
        var radius = sporeMessage.Radius;

        var underneathPlayer = false;
        if (players.ContainsKey(((int)gameManager.ClientId)))
        {
            var player = players[((int)gameManager.ClientId)];
            var playerPos = new Vector2(player.Position.X, player.Position.Y);
            var sporePos = new Vector2(((int)x), ((int)y));
            underneathPlayer = playerPos.DistanceSquaredTo(sporePos) < player.Radius * player.Radius;
        }

        if (!spores.ContainsKey((int) sporeId))
        {
            var spore = Spore.Instantiate(
                ((int)sporeId),
                ((float)x),
                ((float)y),
                ((float)radius),
                underneathPlayer
            );
            world.AddChild(spore);
            spores[(int)sporeId] = spore;
        }
    }

    private void _handleSporeBatchMessage(ulong senderId, SporeBatchMessage sporeBatch)
    {
        foreach (var sporeMsg in sporeBatch.Spores)
        {
            _handleSporeMessage(senderId, sporeMsg);
        }
    }

    private void _handleSporeConsumedMessage(ulong senderId, SporeConsumedMessage sporeConsumed)
    {
        if (players.ContainsKey((int)senderId))
        {
            var actor = players[(int)senderId];
            var actorMass = _radToMass(actor.Radius);

            var sporeId = (int)sporeConsumed.SporeId;
            if (spores.ContainsKey(sporeId))
            {
                var spore = spores[sporeId];
                var sporeMass = _radToMass(spore.Radius);

                _setActorMass(actor, actorMass + sporeMass);
                _removeSpore(spore);

            }
        }
    }

    private void _handleDisconnectMessage(ulong senderId, DisconnectMessage disconnect)
    {
        if (players.ContainsKey(((int)senderId)))
        {
            var actor = players[((int)senderId)];
            var reason = disconnect.Reason;
            log.Info($"{actor.ActorName} disconnected because {reason}");
            _removeActor(actor);
        }
    }

    private void _setActorMass(Actor actor, double newMass)
    {
        actor.Radius = (float)Math.Sqrt(newMass / Math.PI);
        hiscores.SetHiscore(actor.ActorName, (int) Math.Floor(newMass));
    }

    private double _radToMass(float radius)
    {
        return radius * radius * Math.PI;
    }

    private void onLineEditTextSubmitted(string newText)
    {
        var packet = new Packet
        {
            Chat = new ChatMessage
            {
                Msg = newText
            }
        };

        var r = WS.SendMessageAsync(packet);
        if (r.IsFaulted)
        {
            log.Error($"Error sending message: {r}");
        } else
        {
            log.Chat("You", newText);
        }

        lineEdit.Clear();
    }
    private void _onPlayerAreaEntered(Area2D area)
    {
        if (area is Spore)
        {
            _consumeSpore(area as Spore);
        }
        else if (area is Actor)
        {
            _collideActor(area as Actor);
        }
    }

    private void _collideActor(Actor actor)
    {
        var player = players[((int)gameManager.ClientId)];
        var playerMass = _radToMass(player.Radius);
        var actorMass = _radToMass(actor.Radius);

        if (playerMass > actorMass)
        {
            _consumeActor(player, actor, playerMass, actorMass);
        } else
        {
            log.Warning($"You collided with {actor.ActorName}, but you were too small to consume them.");
        }
    }

    private void _consumeActor(Actor player, Actor actor, double playerMass, double actorMass)
    {
        _setActorMass(player, playerMass + actorMass);

        var packet = new Packet
        {
            PlayerConsumed = new PlayerConsumedMessage
            {
                PlayerId = (ulong)actor.ActorId
            }
        };

        WS.SendMessageAsync(packet).Wait();

        _removeActor(actor);
    }

    private void _removeActor(Actor actor)
    {
        players.Remove(actor.ActorId);
        actor.QueueFree();
    }

    private void _consumeSpore(Spore spore)
    {
        if (spore.UnderneathPlayer)
        {
            return;
        }

        var packet = new Packet {
            SporeConsumed = new SporeConsumedMessage
            {
                SporeId = (ulong)spore.SporeId
            }
        };

        WS.SendMessageAsync(packet).Wait();

        _removeSpore(spore);
    }

    private void _removeSpore(Spore spore)
    {
        spores.Remove(spore.SporeId);
        spore.QueueFree();
    }
}