using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace STS2Advisor;

[ModInitializer("Initialize")]
public static class STS2AdvisorMod
{
    private static AdvisorOverlay? _overlay;

    public static void Initialize()
    {
        Log.Warn("STS2 Advisor loaded");

        var harmony = new Harmony("sts2advisor.patch");
        harmony.PatchAll();

        TryCreateOverlay();
    }

    private static void TryCreateOverlay()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
        {
            Log.Warn("STS2 Advisor: SceneTree not ready, overlay not created yet.");
            return;
        }

        var existing = tree.Root.GetNodeOrNull<AdvisorOverlay>("STS2AdvisorOverlay");
        if (existing != null)
        {
            _overlay = existing;
            return;
        }

        _overlay = new AdvisorOverlay
        {
            Name = "STS2AdvisorOverlay"
        };

        tree.Root.CallDeferred(Node.MethodName.AddChild, _overlay);
    }

    public static void SetOverlayText(string text)
    {
        _overlay?.SetRecommendation(text);
    }
}