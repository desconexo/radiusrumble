using Client;
using Client.Packets;
using Godot;
using System;

public partial class BrowsingHiscores : Node
{
    private GameManager gameManager;
    private WebSocket WS;
    private Hiscores hiscores;
    private Button backButton;
    private Button searchButton;
    private LineEdit searchField;
    private Log log;

    public override void _Ready()
    {
        gameManager = GetNode<GameManager>("/root/GameManager");
        WS = GetNode<WebSocket>("/root/WS");
        hiscores = GetNode<Hiscores>("UI/VBoxContainer/Hiscores");
        log = GetNode<Log>("UI/VBoxContainer/Log");
        backButton = GetNode<Button>("UI/VBoxContainer/HBoxContainer/BackButton");
        searchButton = GetNode<Button>("UI/VBoxContainer/HBoxContainer/SearchButton");
        searchField = GetNode<LineEdit>("UI/VBoxContainer/HBoxContainer/SearchField");

        backButton.Connect("pressed", Callable.From(_onBackButtonPressed));
        searchButton.Connect("pressed", Callable.From(_onSearchButtonPressed));
        searchField.Connect("text_submitted", Callable.From<string>(_onSearchFieldSubmitted));
        WS.Connect("OnPacketReceived", Callable.From<string>(onWebSocketPacketReceived));

        var packet = new Packet { HiscoreBoardRequest = new HiscoreBoardRequestMessage { } };
        WS.SendMessageAsync(packet).Wait();
    }

    private void _onSearchButtonPressed()
    {
        var packet = new Packet
        {
            SearchHiscore = new SearchHiscoreMessage
            {
                Name = searchField.Text.Trim()
            }
        };

        WS.SendMessageAsync(packet).Wait();
    }

    private void _onSearchFieldSubmitted(string text)
    {
        _onSearchButtonPressed();
    }

    private void _onBackButtonPressed()
    {
        var packet = new Packet { 
            FinishBrowsingHiscores = new FinishedBrowsingHiscoresMessage { }
        };

        WS.SendMessageAsync(packet).Wait();
        gameManager.SetState(GameManager.State.CONNECTED);
    }

    private void onWebSocketPacketReceived(string payload)
    {
        var bytes = Convert.FromBase64String(payload);
        var packet = Packet.Parser.ParseFrom(bytes);

        if (packet.HiscoreBoard != null)
        {
            _handleHiscoreBoardMessage(packet.HiscoreBoard);
        } else if (packet.DenyResponse != null)
        { 
            _handleDenyResponse(packet.DenyResponse);
        }
    }

    private void _handleDenyResponse(DenyResponseMessage denyResponse)
    {
        log.Error(denyResponse.Msg);
    }

    private void _handleHiscoreBoardMessage(HiscoreBoardMessage hiscoreBoard)
    {
        hiscores.ClearHiscores();
        foreach (var hiscoreMessage in hiscoreBoard.Hiscores)
        {
            var name = hiscoreMessage.Name;
            var rankAndName = $"{hiscoreMessage.Rank}. {name}";
            var score = hiscoreMessage.Score;
            var highlight = name.ToLower() == searchField.Text.Trim().ToLower();
            hiscores.SetHiscore(rankAndName, ((int)score), highlight);
        }
    }
}
