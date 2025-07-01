using Godot;
using System;

public partial class Spore : Area2D
{
    static private PackedScene SporeScene = GD.Load<PackedScene>("res://objects/spore/spore.tscn");

    private int sporeId;
    public int SporeId
    {
        get => sporeId;
        private set => sporeId = value;
    }
    private float startX;
    private float startY;

    private float radius;
    public float Radius
    {
        get => radius;
        set
        {
            radius = value;
            collisionShape.Radius = radius;
        }
    }
    private Color color;
    private bool underneathPlayer;
    public bool UnderneathPlayer
    {
        get => underneathPlayer;
    }

    static public Spore Instantiate(int sporeId, float startX, float startY, float radius, bool underneathPlayer)
    {
        var spore = SporeScene.Instantiate<Spore>();
        spore.sporeId = sporeId;
        spore.startX = startX;
        spore.startY = startY;
        spore.radius = radius;
        spore.underneathPlayer = underneathPlayer;

        return spore;

    }

    private CircleShape2D collisionShape;

    public override void _Ready()
    {
        if (underneathPlayer)
        {
            Connect("area_exited", Callable.From<Area2D>(_onAreaExited));
        }

        var cs = GetNode<CollisionShape2D>("CollisionShape2D");
        collisionShape = (CircleShape2D)cs.Shape;

        Position = new Vector2(startX, startY);
        collisionShape.Radius = radius;
        color = Color.FromHsv((float) new Random().NextDouble(), 1, 1, 1);
    }

    private void _onAreaExited(Area2D area)
    {
        if (area is Actor)
        {
            underneathPlayer = false;
        }
    }

    public override void _Draw()
    {
        DrawCircle(Vector2.Zero, radius, color);
    }
}
