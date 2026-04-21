using Godot;

namespace STS2Advisor;

public partial class AdvisorOverlay : CanvasLayer
{
    private PanelContainer? _panel;
    private Label? _label;

    public override void _Ready()
    {
        Name = "STS2AdvisorOverlay";
        Layer = 100;

        _panel = new PanelContainer
        {
            Position = new Vector2(20, 20),
            CustomMinimumSize = new Vector2(420, 80)
        };

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_bottom", 10);

        _label = new Label
        {
            Text = "Advisor: hardcoded test recommendation",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _label.AddThemeFontSizeOverride("font_size", 22);
        _label.Modulate = new Color(1.0f, 0.95f, 0.60f);

        margin.AddChild(_label);
        _panel.AddChild(margin);
        AddChild(_panel);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey key
            && key.Pressed
            && !key.Echo
            && key.Keycode == Key.F7)
        {
            Visible = !Visible;
        }
    }

    public void SetRecommendation(string text)
    {
        if (_label != null)
            _label.Text = text;
    }
}