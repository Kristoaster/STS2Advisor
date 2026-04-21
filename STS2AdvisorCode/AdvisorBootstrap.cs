using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace STS2Advisor;

public partial class AdvisorBootstrap : Node
{
    private AdvisorOverlay? _overlay;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Log.Warn("STS2 Advisor bootstrap ready");
    }

    public override void _Process(double delta)
    {
        EnsureOverlay();
    }

    private void EnsureOverlay()
    {
        var root = GetTree()?.Root;
        if (root == null)
            return;

        if (_overlay != null && IsInstanceValid(_overlay))
            return;

        var existing = root.GetNodeOrNull<AdvisorOverlay>("STS2AdvisorOverlay");
        if (existing != null)
        {
            _overlay = existing;
            return;
        }

        _overlay = new AdvisorOverlay
        {
            Name = "STS2AdvisorOverlay"
        };

        root.AddChild(_overlay);
        Log.Warn("STS2 Advisor overlay created");
    }

    public void SetRecommendation(string text)
    {
        _overlay?.SetRecommendation(text);
    }
}