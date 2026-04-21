using System.Text;
using Godot;
using HarmonyLib;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
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

            var overlayText = TryBuildCardRewardOverlay(runState);
            if (overlayText != null)
            {
                _label.Text = overlayText;
                return;
            }

            overlayText = TryBuildCombatOverlay(runState);
            if (overlayText != null)
            {
                _label.Text = overlayText;
                return;
            }

            _label.Text = $"Advisor\n{runState.CurrentRoom?.GetType().Name ?? "Unknown room"}";
        }
        catch (System.Exception ex)
        {
            _label.Text = $"Advisor\nError: {ex.GetType().Name}";
            Log.Warn($"STS2 Advisor refresh failed: {ex}");
        }
    }
    
    private static string? TryBuildCombatOverlay(RunState runState)
    {
        var currentRoom = runState.CurrentRoom;

        if (currentRoom is not CombatRoom || !CombatManager.Instance.IsInProgress)
            return null;

        var combatState = CombatManager.Instance.DebugOnlyGetState();
        var player = LocalContext.GetMe(runState);

        if (combatState == null || player == null || player.PlayerCombatState == null)
            return "Advisor\nCombat active, but combat state is unavailable";

        int livingEnemies = 0;
        foreach (var enemy in combatState.Enemies)
        {
            if (enemy.IsAlive)
                livingEnemies++;
        }

        int handCount = 0;
        foreach (var _ in player.PlayerCombatState.Hand.Cards)
            handCount++;

        var recommendation = BuildBasicCombatRecommendation(player, combatState);

        var sb = new StringBuilder();
        sb.AppendLine("Advisor");
        sb.AppendLine(
            $"Combat | Energy {player.PlayerCombatState.Energy}/{player.PlayerCombatState.MaxEnergy} | " +
            $"HP {player.Creature.CurrentHp}/{player.Creature.MaxHp} | " +
            $"Block {player.Creature.Block}"
        );
        sb.AppendLine($"Hand {handCount} | Enemies {livingEnemies}");
        sb.Append(recommendation);

        return sb.ToString();
    }
    
    private static string? TryBuildCardRewardOverlay(RunState runState)
    {
        var topOverlay = NOverlayStack.Instance?.Peek();
        if (topOverlay is not Node overlayNode)
            return null;

        if (overlayNode.GetType().Name != "NCardRewardSelectionScreen")
            return null;

        var player = LocalContext.GetMe(runState);
        if (player == null)
            return "Advisor\nCard reward\nPlayer unavailable";

        var profile = BuildIroncladDeckProfile(player);
        var offeredCards = GetRewardCards(overlayNode);

        if (offeredCards.Count == 0)
            return "Advisor\nCard reward\nNo cards found";

        CardModel? bestCard = null;
        int bestCardScore = int.MinValue;
        string bestReason = "skip";

        foreach (var card in offeredCards)
        {
            int score = ScoreIroncladRewardCard(card, profile, runState.TotalFloor, out var reason);

            if (score > bestCardScore)
            {
                bestCard = card;
                bestCardScore = score;
                bestReason = reason;
            }
        }

        int skipScore = ScoreSkip(profile, runState.TotalFloor);

        var sb = new StringBuilder();
        sb.AppendLine("Advisor");
        sb.AppendLine($"Card reward | Plan {profile.Plan} | Deck {profile.DeckSize}");

        if (bestCard != null && bestCardScore > skipScore)
            sb.Append($"Recommendation: Take {GetCardDisplayName(bestCard)} ({bestReason})");
        else
            sb.Append("Recommendation: Skip");

        return sb.ToString();
    }
    
    private static List<CardModel> GetRewardCards(Node cardRewardScreen)
    {
        var results = new List<CardModel>();
        var holders = FindAllNodesOfType<NCardHolder>(cardRewardScreen);

        foreach (var holder in holders)
        {
            var card = holder.CardModel;
            if (card == null)
                continue;

            if (!ContainsCardWithSameId(results, card))
                results.Add(card);
        }

        return results;
    }

    private static bool ContainsCardWithSameId(List<CardModel> cards, CardModel candidate)
    {
        var candidateId = GetCardDisplayName(candidate);

        foreach (var card in cards)
        {
            if (GetCardDisplayName(card) == candidateId)
                return true;
        }

        return false;
    }

    private static List<T> FindAllNodesOfType<T>(Node root) where T : Node
    {
        var results = new List<T>();
        CollectNodesOfType(root, results);
        return results;
    }

    private static void CollectNodesOfType<T>(Node node, List<T> results) where T : Node
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is T match)
                results.Add(match);

            CollectNodesOfType(child, results);
        }
    }
    
    private static string BuildBasicCombatRecommendation(MegaCrit.Sts2.Core.Entities.Players.Player player,
        CombatState combatState) 
    {
        var playerCombatState = player.PlayerCombatState; 
        if (playerCombatState == null) 
            return "Recommendation: State unavailable";
        
        if (playerCombatState.Energy <= 0) 
            return "Recommendation: End turn";
        
        CardModel? bestAttack = null; 
        int bestAttackScore = int.MinValue;
        
        CardModel? bestSkill = null; 
        int bestSkillScore = int.MinValue;
        
        CardModel? lethalAttack = null; 
        int lethalAttackScore = int.MinValue;
        
        int livingEnemies = 0; 
        foreach (var enemy in combatState.Enemies) 
        { 
            if (enemy.IsAlive) 
                livingEnemies++; 
        }
        
        foreach (var card in playerCombatState.Hand.Cards) 
        { 
            card.CanPlay(out var unplayableReason, out _); 
            if (unplayableReason != UnplayableReason.None) 
                continue;
            
            int score = GetSimpleCardScore(card);
            
            if (card.Type == CardType.Attack) 
            { 
                if (CouldBeLethal(card, combatState) && score > lethalAttackScore) 
                { 
                    lethalAttack = card; 
                    lethalAttackScore = score; 
                }
                
                if (score > bestAttackScore) 
                { 
                    bestAttack = card; 
                    bestAttackScore = score; 
                } 
            }
            else if (card.Type == CardType.Skill) 
            { 
                if (score > bestSkillScore) 
                { 
                    bestSkill = card; 
                    bestSkillScore = score; 
                } 
            } 
        }
        
        if (lethalAttack != null) 
            return $"Recommendation: Play {GetCardDisplayName(lethalAttack)} for lethal";
        
        bool shouldRespectPressure = 
            livingEnemies >= 2 ||
            
            player.Creature.Block <= 0;
        
        if (shouldRespectPressure && bestSkill != null) 
            return $"Recommendation: Play {GetCardDisplayName(bestSkill)}";
        
        if (bestAttack != null) 
            return $"Recommendation: Play {GetCardDisplayName(bestAttack)}";
        
        if (bestSkill != null) 
            return $"Recommendation: Play {GetCardDisplayName(bestSkill)}";
        
        return "Recommendation: End turn"; 
    }
    
    private static bool CouldBeLethal(CardModel card, CombatState combatState)
    {
        try
        {
            if (card.Type != CardType.Attack)
                return false;

            int roughDamage = EstimateRoughAttackDamage(card);
            if (roughDamage <= 0)
                return false;

            foreach (var enemy in combatState.Enemies)
            {
                if (!enemy.IsAlive)
                    continue;

                if (roughDamage >= enemy.CurrentHp + enemy.Block)
                    return true;
            }
        }
        catch
        {
        }

        return false;
    }
    
    private static int EstimateRoughAttackDamage(CardModel card)
    {
        try
        {
            var text = card.Id.Entry.ToLowerInvariant();

            if (text.Contains("bash")) return 8;
            if (text.Contains("strike")) return 6;
            if (text.Contains("neutralize")) return 3;
            if (text.Contains("survivor")) return 0;
            if (text.Contains("defend")) return 0;
        }
        catch
        {
        }

        if (card.Type == CardType.Attack)
            return 6;

        return 0;
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
    
    private enum IroncladPlan
    {
        Unknown,
        Strength,
        Exhaust,
        Block,
        SelfDamage,
        Hybrid
    }

    private sealed class IroncladDeckProfile
    {
        public IroncladPlan Plan { get; init; } = IroncladPlan.Unknown;
        public int DeckSize { get; init; }

        public int AttackCount { get; init; }
        public int SkillCount { get; init; }
        public int PowerCount { get; init; }

        public int BlockCards { get; init; }
        public int FrontloadCards { get; init; }
        public int ScalingCards { get; init; }
        public int ExhaustPayoffs { get; init; }
        public int ExhaustEnablers { get; init; }
        public int StrengthSources { get; init; }
        public int StrengthPayoffs { get; init; }
        public int SelfDamageCards { get; init; }
        public int DrawCards { get; init; }
        public int AoeCards { get; init; }

        public bool NeedsFrontload { get; init; }
        public bool NeedsBlock { get; init; }
        public bool NeedsScaling { get; init; }
        public bool CanSkipAggressively { get; init; }
    }
    
    private static IroncladDeckProfile BuildIroncladDeckProfile(MegaCrit.Sts2.Core.Entities.Players.Player player) 
    { 
        int deckSize = 0; 
        int attackCount = 0; 
        int skillCount = 0; 
        int powerCount = 0; 
        int blockCards = 0; 
        int frontloadCards = 0; 
        int scalingCards = 0; 
        int exhaustPayoffs = 0; 
        int exhaustEnablers = 0; 
        int strengthSources = 0; 
        int strengthPayoffs = 0; 
        int selfDamageCards = 0; 
        int drawCards = 0; 
        int aoeCards = 0;
        
        foreach (var card in player.Deck.Cards)
        {
            deckSize++;
            var id = GetCardDisplayName(card).ToLowerInvariant();

            if (card.Type == CardType.Attack) attackCount++;
            if (card.Type == CardType.Skill) skillCount++;
            if (card.Type == CardType.Power) powerCount++;

            if (LooksLikeBlockCard(id)) blockCards++;
            if (LooksLikeFrontloadCard(id)) frontloadCards++;
            if (LooksLikeScalingCard(id)) scalingCards++;
            if (LooksLikeExhaustPayoff(id)) exhaustPayoffs++;
            if (LooksLikeExhaustEnabler(id)) exhaustEnablers++;
            if (LooksLikeStrengthSource(id)) strengthSources++;
            if (LooksLikeStrengthPayoff(id)) strengthPayoffs++;
            if (LooksLikeSelfDamageCard(id)) selfDamageCards++;
            if (LooksLikeDrawCard(id)) drawCards++;
            if (LooksLikeAoeCard(id)) aoeCards++;
        }
        
        var plan = DetermineIroncladPlan(
            strengthSources, strengthPayoffs, 
            exhaustPayoffs, exhaustEnablers, 
            blockCards, selfDamageCards
            );
        
        return new IroncladDeckProfile 
        { 
            Plan = plan, 
            DeckSize = deckSize, 
            AttackCount = attackCount, 
            SkillCount = skillCount, 
            PowerCount = powerCount, 
            BlockCards = blockCards, 
            FrontloadCards = frontloadCards, 
            ScalingCards = scalingCards, 
            ExhaustPayoffs = exhaustPayoffs, 
            ExhaustEnablers = exhaustEnablers, 
            StrengthSources = strengthSources, 
            StrengthPayoffs = strengthPayoffs, 
            SelfDamageCards = selfDamageCards, 
            DrawCards = drawCards, 
            AoeCards = aoeCards,
            
            NeedsFrontload = frontloadCards < 4, 
            NeedsBlock = blockCards < 4, 
            NeedsScaling = scalingCards < 3, 
            CanSkipAggressively = deckSize >= 18 && blockCards >= 4 && frontloadCards >= 4 && scalingCards >= 2 
        }; 
    }
    
    private static int ScoreIroncladRewardCard(
        CardModel card, 
        IroncladDeckProfile profile, 
        int floor, 
        out string reason) 
    { 
        var id = GetCardDisplayName(card).ToLowerInvariant(); 
        int score = 0;
        
        bool early = floor <= 16;
        
        if (LooksLikeFrontloadCard(id)) 
        { 
            score += early ? 30 : 12; 
            if (profile.NeedsFrontload) 
                score += 20; 
        }
        
        if (LooksLikeBlockCard(id)) 
        { 
            score += profile.NeedsBlock ? 32 : 12; 
        }
        
        if (LooksLikeScalingCard(id)) 
        { 
            score += profile.NeedsScaling ? 28 : 10; 
        }
        
        if (LooksLikeDrawCard(id)) 
        { 
            score += 16; 
        }
        
        if (LooksLikeAoeCard(id)) 
        { 
            score += early ? 18 : 10; 
            if (profile.AoeCards == 0) 
                score += 8; 
        }
        
        if (profile.Plan == IroncladPlan.Strength) 
        { 
            if (LooksLikeStrengthSource(id) || LooksLikeStrengthPayoff(id)) 
                score += 22; 
        }
        
        if (profile.Plan == IroncladPlan.Exhaust) 
        { 
            if (LooksLikeExhaustPayoff(id) || LooksLikeExhaustEnabler(id)) 
                score += 22; 
        }
        
        if (profile.Plan == IroncladPlan.Block) 
        { 
            if (LooksLikeBlockCard(id)) 
                score += 18; 
        }
        
        if (profile.Plan == IroncladPlan.SelfDamage) 
        { 
            if (LooksLikeSelfDamageCard(id)) 
                score += 14; 
        }
        
        if (LooksLikeSelfDamageCard(id) && profile.Plan == IroncladPlan.Unknown) 
            score -= 10;
        
        if (profile.DeckSize >= 18 && score < 25) 
            score -= 12;
        
        reason = 
            score >= 45 ? "high impact" : 
            score >= 30 ? "good fit" : 
            score >= 15 ? "playable" : 
            "weak fit";
        
        return score; 
    }
    
    private static int ScoreSkip(IroncladDeckProfile profile, int floor) 
    { 
        int score = 0;
        
        if (profile.CanSkipAggressively) 
            score += 35;
        
        if (profile.DeckSize >= 20) 
            score += 20;
        
        if (floor >= 20) 
            score += 10;
        
        if (profile.NeedsFrontload) score -= 18; 
        if (profile.NeedsBlock) score -= 18; 
        if (profile.NeedsScaling) score -= 18;
        
        return score; 
    }
    
    private static IroncladPlan DetermineIroncladPlan(
        int strengthSources, int strengthPayoffs,
        int exhaustPayoffs, int exhaustEnablers,
        int blockCards, int selfDamageCards)
    {
        bool strength = strengthSources + strengthPayoffs >= 3;
        bool exhaust = exhaustPayoffs + exhaustEnablers >= 3;
        bool block = blockCards >= 5;
        bool selfDamage = selfDamageCards >= 3;

        int activePlans = 0;
        if (strength) activePlans++;
        if (exhaust) activePlans++;
        if (block) activePlans++;
        if (selfDamage) activePlans++;

        if (activePlans >= 2) return IroncladPlan.Hybrid;
        if (strength) return IroncladPlan.Strength;
        if (exhaust) return IroncladPlan.Exhaust;
        if (block) return IroncladPlan.Block;
        if (selfDamage) return IroncladPlan.SelfDamage;
        return IroncladPlan.Unknown;
    }
    
    private static bool LooksLikeBlockCard(string id) =>
        id.Contains("defend") || id.Contains("shrug") || id.Contains("flame_barrier") ||
        id.Contains("impervious") || id.Contains("power_through") || id.Contains("armaments");

    private static bool LooksLikeFrontloadCard(string id) =>
        id.Contains("bash") || id.Contains("strike") || id.Contains("uppercut") ||
        id.Contains("carnage") || id.Contains("hemokinesis") || id.Contains("pommel");

    private static bool LooksLikeScalingCard(string id) =>
        id.Contains("inflame") || id.Contains("demon_form") || id.Contains("spot_weakness") ||
        id.Contains("feel_no_pain") || id.Contains("dark_embrace") || id.Contains("barricade");

    private static bool LooksLikeExhaustPayoff(string id) =>
        id.Contains("feel_no_pain") || id.Contains("dark_embrace") || id.Contains("corruption");

    private static bool LooksLikeExhaustEnabler(string id) =>
        id.Contains("burning_pact") || id.Contains("second_wind") || id.Contains("true_grit") ||
        id.Contains("fiend_fire") || id.Contains("corruption");

    private static bool LooksLikeStrengthSource(string id) =>
        id.Contains("inflame") || id.Contains("demon_form") || id.Contains("spot_weakness") ||
        id.Contains("limit_break");

    private static bool LooksLikeStrengthPayoff(string id) =>
        id.Contains("heavy_blade") || id.Contains("sword_boomerang") ||
        id.Contains("pummel") || id.Contains("twin_strike");

    private static bool LooksLikeSelfDamageCard(string id) =>
        id.Contains("hemokinesis") || id.Contains("bloodletting") || id.Contains("combust") ||
        id.Contains("rupture");

    private static bool LooksLikeDrawCard(string id) =>
        id.Contains("pommel") || id.Contains("battle_trance") || id.Contains("burning_pact") ||
        id.Contains("dark_embrace");

    private static bool LooksLikeAoeCard(string id) =>
        id.Contains("cleave") || id.Contains("whirlwind") || id.Contains("thunderclap");
}