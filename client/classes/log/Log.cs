using Godot;
using System;

public partial class Log : RichTextLabel
{
    private void _message(String message, Color color = default)
    {
        if (color == default)
        {
            color = Colors.White;
        }
        AppendText($"[color=#{color.ToHtml()}]{message}[/color]\n");
        //Pop();
    }

    public void Info(String message)
    {
        _message(message, Colors.LightBlue);
    }

    public void Warning(String message)
    {
        _message(message, Colors.Yellow);
    }

    public void Error(String message)
    {
        _message(message, Colors.Red);
    }

    public void Success(String message)
    {
        _message(message, Colors.Green);
    }

    public void Chat(String senderName, String message)
    {
        _message($"[color=#{Colors.CornflowerBlue.ToHtml()}]{senderName}[/color]: [i]{message}[/i]", Colors.White);
    }
}
