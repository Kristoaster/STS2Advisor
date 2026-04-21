using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace STS2Advisor;

[ModInitializer("Initialize")]
public static class STS2AdvisorMod
{
    private static CanvasLayer? _layer;
    private static Label? _label;

    public static void Initialize()
    {
        Log.Warn("STS2 Advisor loaded");

        var harmony = new Harmony("sts2advisor.patch");
        harmony.PatchAll();

        if (Engine.GetMainLoop() is not SceneTree tree)
        {
            Log.Warn("STS2 Advisor: SceneTree not ready.");
            return;
        }

        tree.Root.CallDeferred(Node.MethodName.AddChild, BuildOverlay());
        Log.Warn("STS2 Advisor: built-in overlay queued");
    }

    private static CanvasLayer BuildOverlay()
    {
        _layer = new CanvasLayer();
        _layer.Layer = 100;

        var panel = new PanelContainer();
        panel.Position = new Vector2(20, 20);
        panel.CustomMinimumSize = new Vector2(420, 80);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_bottom", 10);

        _label = new Label();
        _label.Text = "Advisor: built-in node test";
        _label.AutowrapMode = TextServer.AutowrapMode.WordSmart;

        margin.AddChild(_label);
        panel.AddChild(margin);
        _layer.AddChild(panel);

        Log.Warn("STS2 Advisor: built-in overlay constructed");
        return _layer;
    }

    public static void SetOverlayText(string text)
    {
        if (_label != null)
            _label.Text = text;
    }
}