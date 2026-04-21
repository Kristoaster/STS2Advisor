using System.Text;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Modding;

namespace STS2Advisor;

[ModInitializer("Initialize")]
public static class STS2AdvisorMod
{
    private static CanvasLayer? _layer;
    private static Label? _label;
    private static Godot.Timer? _refreshTimer;

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
        tree.Root.CallDeferred(Node.MethodName.AddChild, BuildRefreshTimer());

        Log.Warn("STS2 Advisor: overlay + timer queued");
    }

    private static CanvasLayer BuildOverlay()
    {
        _layer = new CanvasLayer();
        _layer.Layer = 100;

        var panel = new PanelContainer();
        panel.Position = new Vector2(20, 20);
        panel.CustomMinimumSize = new Vector2(560, 110);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_bottom", 10);

        _label = new Label();
        _label.Text = "Advisor: starting...";
        _label.AutowrapMode = TextServer.AutowrapMode.WordSmart;

        margin.AddChild(_label);
        panel.AddChild(margin);
        _layer.AddChild(panel);

        Log.Warn("STS2 Advisor: built-in overlay constructed");
        return _layer;
    }

    private static Godot.Timer BuildRefreshTimer()
    {
        _refreshTimer = new Godot.Timer();
        _refreshTimer.WaitTime = 0.25;
        _refreshTimer.OneShot = false;
        _refreshTimer.Autostart = true;
        _refreshTimer.ProcessMode = Node.ProcessModeEnum.Always;
        _refreshTimer.Timeout += RefreshOverlay;

        Log.Warn("STS2 Advisor: refresh timer constructed");
        return _refreshTimer;
    }

    private static void RefreshOverlay()
    {
        if (_label == null)
            return;

        try
        {
            if (!RunManager.Instance.IsInProgress)
            {
                _label.Text = "Advisor\nNo run in progress";
                return;
            }

            var runState = RunManager.Instance.DebugOnlyGetState();
            if (runState == null)
            {
                _label.Text = "Advisor\nRun state unavailable";
                return;
            }

            var currentRoom = runState.CurrentRoom;

            if (currentRoom is CombatRoom && CombatManager.Instance.IsInProgress)
            {
                var combatState = CombatManager.Instance.DebugOnlyGetState();
                var player = LocalContext.GetMe(runState);

                if (combatState == null || player == null || player.PlayerCombatState == null)
                {
                    _label.Text = "Advisor\nCombat active, but combat state is unavailable";
                    return;
                }

                int livingEnemies = 0;
                foreach (var enemy in combatState.Enemies)
                {
                    if (enemy.IsAlive)
                        livingEnemies++;
                }

                int handCount = 0;
                foreach (var _ in player.PlayerCombatState.Hand.Cards)
                    handCount++;

                var recommendation = BuildBasicCombatRecommendation(player);

                var sb = new StringBuilder();
                sb.AppendLine("Advisor");
                sb.AppendLine(
                    $"Combat | Energy {player.PlayerCombatState.Energy}/{player.PlayerCombatState.MaxEnergy} | " +
                    $"HP {player.Creature.CurrentHp}/{player.Creature.MaxHp} | " +
                    $"Block {player.Creature.Block}"
                );
                sb.AppendLine($"Hand {handCount} | Enemies {livingEnemies}");
                sb.Append(recommendation);

                _label.Text = sb.ToString();
                return;
            }

            _label.Text = $"Advisor\n{currentRoom?.GetType().Name ?? "Unknown room"}";
        }
        catch (System.Exception ex)
        {
            _label.Text = $"Advisor\nError: {ex.GetType().Name}";
            Log.Warn($"STS2 Advisor refresh failed: {ex}");
        }
    }
    
    private static string BuildBasicCombatRecommendation(MegaCrit.Sts2.Core.Entities.Players.Player player)
    {
        var combatState = player.PlayerCombatState;
        if (combatState == null)
            return "Recommendation: State unavailable";

        if (combatState.Energy <= 0)
            return "Recommendation: End turn";

        CardModel? bestAttack = null;
        int bestAttackScore = int.MinValue;

        CardModel? bestSkill = null;
        int bestSkillScore = int.MinValue;

        foreach (var card in combatState.Hand.Cards)
        {
            card.CanPlay(out var unplayableReason, out _);
            if (unplayableReason != UnplayableReason.None)
                continue;

            int score = GetSimpleCardScore(card);

            if (card.Type == CardType.Attack && score > bestAttackScore)
            {
                bestAttack = card;
                bestAttackScore = score;
            }
            else if (card.Type == CardType.Skill && score > bestSkillScore)
            {
                bestSkill = card;
                bestSkillScore = score;
            }
        }

        if (bestAttack != null)
            return $"Recommendation: Play {GetCardDisplayName(bestAttack)}";

        if (bestSkill != null)
            return $"Recommendation: Play {GetCardDisplayName(bestSkill)}";

        return "Recommendation: End turn";
    }

    private static int GetSimpleCardScore(CardModel card)
    {
        int energyScore = card.EnergyCost.CostsX ? 50 : card.EnergyCost.GetAmountToSpend() * 10;
        int upgradedBonus = card.IsUpgraded ? 2 : 0;
        return energyScore + upgradedBonus;
    }

    private static string GetCardDisplayName(CardModel card)
    {
        try
        {
            return card.Id.Entry;
        }
        catch
        {
            return "card";
        }
    }

    public static void SetOverlayText(string text)
    {
        if (_label != null)
            _label.Text = text;
    }
}