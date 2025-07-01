using Client;
using Client.Packets;
using Godot;

public partial class LoginForm : VBoxContainer
{
    private GameManager gameManager;
    private WebSocket WS;
    private LineEdit usernameField;
    private LineEdit passwordField;
    private Button loginButton;
    private Button hiscoresButton;

    [Signal]
    public delegate void onLoginFormSubmittedEventHandler(string username, string password);

    public override void _Ready()
    {
        WS = GetNode<WebSocket>("/root/WS");
        gameManager = GetNode<GameManager>("/root/GameManager");
        usernameField = GetNode<LineEdit>("Username");
        passwordField = GetNode<LineEdit>("Password");
        loginButton = GetNode<Button>("ButtonsContainer/LoginButton");
        hiscoresButton = GetNode<Button>("ButtonsContainer/HiscoresButton");

        loginButton.Connect("pressed", Callable.From(onLoginButtonPressed));
        hiscoresButton.Connect("pressed", Callable.From(onHiscoresButtonPressed));
    }

    private void onLoginButtonPressed()
    {
        EmitSignal("onLoginFormSubmitted", usernameField.Text.Trim(), passwordField.Text.Trim());
    }

    private void onHiscoresButtonPressed()
    {
        gameManager.SetState(GameManager.State.BROWSING_HISCORES);
    }
}
