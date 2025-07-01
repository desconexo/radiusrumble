using Client;
using Client.Packets;
using Godot;
using System;

public partial class Actor : Area2D
{
    static private PackedScene ActorScene = GD.Load<PackedScene>("res://objects/actor/actor.tscn");

    private int actorId;
    public int ActorId
    {
        get => actorId;
        private set => actorId = value;
    }

    private string actorName;
    public string ActorName
    {
        get => actorName;
    }

    private float startX;
    private float startY;
    private float startRad;
    private float speed;
    private bool isPlayer;
    private Color color;
    public Color Color
    {
        get => Color;
        set => Color = value;
    }

    private Vector2 serverPosition;
    public Vector2 ServerPosition
    {
        get => serverPosition;
        set => serverPosition = value;
    }

    private Vector2 velocity;
    public Vector2 Velocity
    {
        get => velocity;
        set => velocity = value;
    }

    private float radius;
    public float Radius
    {
        get => radius;
        set
        {
            radius = value;
            collisionShape.Radius = radius;
            updateZoom();
            QueueRedraw();
        }
    }

    private float targetZoom = 2f;
    private float furthestZoomAllowed = 2f;

    static public Actor Instantiate(
        int actorId, 
        string actorName, 
        float startX, 
        float startY, 
        float startRad, 
        float speed, 
        bool isPlayer, 
        Color color
    )
    {
        var actor = ActorScene.Instantiate<Actor>();
        actor.actorId = actorId;
        actor.actorName = actorName;
        actor.startX = startX;
        actor.startY = startY;
        actor.startRad = startRad;
        actor.speed = speed;
        actor.isPlayer = isPlayer;
        actor.color = color;

        return actor;

    }

    private WebSocket WS;
    private Label nameplate;
    private Camera2D camera;
    private CircleShape2D collisionShape;

    public override void _Ready()
    {
        WS = GetNode<WebSocket>("/root/WS");

        nameplate = GetNode<Label>("Nameplate");
        camera = GetNode<Camera2D>("Camera2D");
        var cs = GetNode<CollisionShape2D>("CollisionShape2D");
        collisionShape = (CircleShape2D) cs.Shape;

        Position = new Vector2(startX, startY);
        velocity = Vector2.Right * speed;
        radius = startRad;

        collisionShape.Radius = radius;
        nameplate.Text = actorName;
    }

    public override void _Process(double delta)
    {
        if (!Mathf.IsEqualApprox(camera.Zoom.X, targetZoom))
        {
            camera.Zoom -= new Vector2(1, 1) * (camera.Zoom.X - targetZoom) * .05f;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        Position += velocity * (float) delta;
        serverPosition += velocity * ((float)delta);
        Position += (serverPosition - Position) * .05f;

        if (!isPlayer)
        {
            return;
        }

        var mousePosition = GetGlobalMousePosition();
        var inputVector = Position.DirectionTo(mousePosition).Normalized();

        if (Math.Abs(velocity.AngleTo(inputVector)) > Math.Tau / 15)
        {
            velocity = inputVector * speed;
            var packet = new Packet
            {
                PlayerDirection = new PlayerDirectionMessage
                {
                    Direction = velocity.Angle()
                }
            };

            WS.SendMessageAsync(packet).ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    GD.PrintErr($"Error sending player direction: {t.Exception}");
                }
            });
        }
    }

    public override void _Draw()
    {
        DrawCircle(Vector2.Zero, radius, color);
    }
    public override void _Input(InputEvent @event)
    {
        if (isPlayer && @event is InputEventMouseButton && @event.IsPressed())
        {
            if (Input.IsMouseButtonPressed(MouseButton.WheelUp))
            {
                targetZoom = Math.Min(30, targetZoom + 0.1f);
                //camera.Zoom = new Vector2(zoom, zoom);
            }
            else if (Input.IsMouseButtonPressed(MouseButton.WheelDown))
            {
                targetZoom = Math.Max(furthestZoomAllowed, targetZoom - 0.1f);
                //camera.Zoom = new Vector2(zoom, zoom);
            }
        }
    }

    private void updateZoom()
    {
        if (IsNodeReady())
        {
            nameplate.AddThemeFontSizeOverride("font_size", Math.Max(16, ((int)(radius / 2))));
        }

        if (!isPlayer)
        {
            return;
        }

        var newFurthestZoomAllowed = 2 * startRad / radius;

        if (Mathf.IsEqualApprox(targetZoom, furthestZoomAllowed))
        {
            targetZoom = newFurthestZoomAllowed;
            furthestZoomAllowed = newFurthestZoomAllowed;
        }

    }
}
