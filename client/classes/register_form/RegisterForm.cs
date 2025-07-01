using Client;
using Godot;
using System;

public partial class RegisterForm : VBoxContainer
{
    private GameManager gameManager;
    private WebSocket WS;
    private LineEdit usernameField;
    private LineEdit passwordField;
    private LineEdit confirmPasswordField;
    private Button confirmButton;
    private Button backButton;
    private ColorPicker colorPicker;

    [Signal]
    public delegate void onRegisterFormSubmittedEventHandler(
        string username, string password, string confirmPassword, Color color);

    [Signal]
    public delegate void onRegisterFormCancelledEventHandler();

    public override void _Ready()
    {
        WS = GetNode<WebSocket>("/root/WS");
        gameManager = GetNode<GameManager>("/root/GameManager");
        
        usernameField = GetNode<LineEdit>("HBoxContainer/VBoxContainer/Username");
        passwordField = GetNode<LineEdit>("HBoxContainer/VBoxContainer/Password");
        confirmPasswordField = GetNode<LineEdit>("HBoxContainer/VBoxContainer/ConfirmPassword");
        colorPicker = GetNode<ColorPicker>("HBoxContainer/ColorPicker");

        confirmButton = GetNode<Button>("ButtonsContainer/ConfirmButton");
        backButton = GetNode<Button>("ButtonsContainer/BackButton");


        confirmButton.Connect("pressed", Callable.From(onConfirmButtonPressed));
        backButton.Connect("pressed", Callable.From(onBackButtonPressed));
    }

    private void onBackButtonPressed()
    {
        EmitSignal("onRegisterFormCancelled");
    }

    private void onConfirmButtonPressed()
    {
        EmitSignal(
            "onRegisterFormSubmitted",
            usernameField.Text.Trim(),
            passwordField.Text.Trim(),
            confirmPasswordField.Text.Trim(),
            colorPicker.Color
        );
    }
}
