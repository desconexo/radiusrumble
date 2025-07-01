using Godot;
using Godot.Collections;
using System;

public partial class GameManager : Node
{
    public enum State
    {
        ENTERED,
        CONNECTED,
        INGAME,
        BROWSING_HISCORES,
    }

    private Dictionary<State, String> _statesScenes = new Dictionary<State, string>
    {
        { State.ENTERED, "res://states/entered/entered.tscn" },
        { State.CONNECTED, "res://states/connected/connected.tscn" },
        { State.INGAME, "res://states/ingame/ingame.tscn" },
        { State.BROWSING_HISCORES, "res://states/browsing_hiscores/browsing_hiscores.tscn" },
    };

    public Dictionary<State, String> StatesScenes
    {
        get => _statesScenes;
        private set
        {
            _statesScenes = value;
        }
    }

    private ulong _clientId;

    public ulong ClientId
    {
        get => _clientId;
        set
        {
            _clientId = value;
        }
    }

    private Node _currentSceneRoot;

    public Node CurrentSceneRoot
    {
        get => _currentSceneRoot;
        private set
        {
            _currentSceneRoot = value;
        }
    }

    public void SetState(State state)
    {
        if (CurrentSceneRoot != null)
        {
            CurrentSceneRoot.QueueFree();
        }

        PackedScene scene = ResourceLoader.Load<PackedScene>(_statesScenes[state]);
        CurrentSceneRoot = scene.Instantiate();
        AddChild(CurrentSceneRoot);
    }
}
