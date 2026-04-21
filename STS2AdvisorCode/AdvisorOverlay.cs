using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace STS2Advisor;

public partial class AdvisorOverlay : CanvasLayer
{
    private Label? _label;

    public override void _Ready()
    {
        Log.Warn("STS2 Advisor overlay entering _Ready");

        _label = new Label();
        _label.Text = "Advisor: test overlay";
        _label.Position = new Vector2(20, 20);

        AddChild(_label);

        Log.Warn("STS2 Advisor overlay ready");
    }

    public void SetRecommendation(string text)
    {
        if (_label != null)
            _label.Text = text;
    }
}