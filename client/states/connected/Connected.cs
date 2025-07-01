using Client;
using Client.Packets;
using Godot;
using System;

public partial class Connected : Node
{
    private GameManager gameManager;
    private WebSocket WS;
    private Log log;
    private LoginForm loginForm;
    private RegisterForm registerForm;
    private RichTextLabel registerPrompt;

    Callable _actionOnOkReceived;

    public override void _Ready()
    {
        gameManager = GetNode<GameManager>("/root/GameManager");
        log = GetNode<Log>("UI/MarginContainer/FormContainer/Log");
        loginForm = GetNode<LoginForm>("UI/MarginContainer/FormContainer/LoginForm");
        registerForm = GetNode<RegisterForm>("UI/MarginContainer/FormContainer/RegisterForm");
        registerPrompt = GetNode<RichTextLabel>("UI/MarginContainer/FormContainer/RegisterPrompt");
        WS = GetNode<WebSocket>("/root/WS");

        WS.Connect("OnConnectionClosed", Callable.From(onWebSocketConnectionClosed));
        WS.Connect("OnPacketReceived", Callable.From<string>(onWebSocketPacketReceived));
        //registerButton.Connect("pressed", Callable.From(onRegisterButtonPressed));
        loginForm.Connect("onLoginFormSubmitted", Callable.From<string, string>(onLoginSubmitted));
        registerForm.Connect("onRegisterFormSubmitted", Callable.From<string, string, string, Color>(onRegisterFormSubmitted));
        registerForm.Connect("onRegisterFormCancelled", Callable.From(onRegisterFormBackButtonClicked));
        registerPrompt.Connect("meta_clicked", Callable.From<string>(onRegisterPromptMetaClicked));

    }

    private void onRegisterPromptMetaClicked(string meta)
    {
        if (meta == "register")
        {
            log.Clear();

            loginForm.Hide();
            registerPrompt.Hide();
            registerForm.Show();
        }
    }

    private void onRegisterFormBackButtonClicked()
    {
        log.Clear();
        registerForm.Hide();
        loginForm.Show();
        registerPrompt.Show();
    }

    private void onRegisterFormSubmitted(string username, string password, string confirmPassword, Color color)
    {
        if (password != confirmPassword)
        {
            log.Error("Passwords do not match");
            return;
        }

        var packet = new Packet
        {
            RegisterRequest = new RegisterRequestMessage
            {
                Username = username,
                Password = password,
                Color = color.ToRgba32(),
            }
        };

        WS.SendMessageAsync(packet).ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                log.Error($"Error sending register request: {t.Exception}");
            } else
            {
                _actionOnOkReceived = Callable.From(() =>
                {
                    onRegisterFormBackButtonClicked();
                    log.Success("Registration successful!");
                });
            }
        });
    }

    private void onLoginSubmitted(string username, string password)
    {
        var packet = new Packet
        {
            LoginRequest = new LoginRequestMessage
            {
                Username = username,
                Password = password
            }
        };

        WS.SendMessageAsync(packet).ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                log.Error($"Error sending login request: {t.Exception}");
            } else
            {
                _actionOnOkReceived = Callable.From(() =>
                {
                    gameManager.SetState(GameManager.State.INGAME);
                });
            }
        });
    }

    private void onWebSocketConnectionClosed()
    {
        log.Warning("Connection closed");
    }

    private void onWebSocketPacketReceived(string payload)
    {

        var bytes = Convert.FromBase64String(payload);
        var packet = Packet.Parser.ParseFrom(bytes);

        if (packet.DenyResponse != null)
        {
            var denyResponseMsg = packet.DenyResponse.Msg;
            log.Error(denyResponseMsg);
        }
        else if (packet.OkResponse != null)
        {
            _actionOnOkReceived.Call();
        }
    }
}
