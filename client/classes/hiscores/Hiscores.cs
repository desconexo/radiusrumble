using Godot;
using Godot.Collections;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class Hiscores : ScrollContainer
{
    //private int[] scores = [];
    private List<int> scores = [];
    //private Array<int> scores = new();

    private VBoxContainer vbox;
    private HBoxContainer entryTemplate;

    public override void _Ready()
    {
        vbox = GetNode<VBoxContainer>("MarginContainer/VBoxContainer");
        entryTemplate = GetNode<HBoxContainer>("MarginContainer/VBoxContainer/HBoxContainer");
        entryTemplate.Hide();
    }

    public void SetHiscore(string name, int score, bool highlight = false) {
        removeHiscore(name);
        addHiscore(name, score, highlight);
    }

    private void removeHiscore(string name)
    {
        foreach (var i in Enumerable.Range(0, scores.Count()))
        {
            var entry = vbox.GetChild(i) as HBoxContainer;
            var nameLabel = entry.GetChild<Label>(0);

            if (nameLabel.Text == name)
            {
                scores.RemoveAt(scores.Count() - i - 1);
                entry.Free();
                return;
            }
        }
    }

    private void addHiscore(string name, int score, bool highlight = false)
    {
        scores.Add(score);
        scores.Sort((x, y) => x.CompareTo(y));

        var pos = scores.Count() - scores.IndexOf(score) - 1;
        var entry = entryTemplate.Duplicate() as HBoxContainer;
        var nameLabel = entry.GetChild<Label>(0);
        var scoreLabel = entry.GetChild<Label>(1);

        vbox.AddChild(entry);
        vbox.MoveChild(entry, pos);

        nameLabel.Text = name;
        scoreLabel.Text = score.ToString();

        entry.Show();

        if (highlight)
        {
            nameLabel.AddThemeColorOverride("font_color", Colors.Yellow);
        }
    }
    public void ClearHiscores()
    {
        scores.Clear();
        foreach (var entry in vbox.GetChildren())
        {
            if (entry != entryTemplate)
            {
                entry.Free();
            }
        }
    }
}
