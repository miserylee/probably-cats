using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace DeepSpaceCattery;

public sealed partial class GameMod
{
    private void EnableNativeCatInteractions() => NativeCatInteraction.Enabled = true;

    private void DisableNativeCatInteractions() => NativeCatInteraction.Enabled = false;
}

internal static class NativeCatInteraction
{
    private sealed class TargetRule
    {
        public Func<GameItem, bool> Matches { get; }
        public Func<GameItem, GameItem, bool> CanApply { get; }
        public Func<GameItem, GameItem, bool> Apply { get; }

        public TargetRule(Func<GameItem, bool> matches, Func<GameItem, GameItem, bool> canApply,
            Func<GameItem, GameItem, bool> apply)
        {
            Matches = matches;
            CanApply = canApply;
            Apply = apply;
        }
    }

    private static readonly ConditionalWeakTable<GameItem, TargetRule> Rules = new();
    internal static bool Enabled { get; set; }

    internal static void Bind(GameItem item, Func<GameItem, bool> matches,
        Func<GameItem, GameItem, bool> canApply, Func<GameItem, GameItem, bool> apply)
    {
        Rules.Remove(item);
        Rules.Add(item, new TargetRule(matches, canApply, apply));
    }

    private static TargetRule? FindRule(GameItem source, GameItem? target) =>
        Enabled && target != null && Rules.TryGetValue(target, out TargetRule? rule) && rule.Matches(source)
            ? rule
            : null;

    private static bool MayUse(GameItem item) => !item.forceDisableUse && item.unitCount > 0 &&
        GameMaster.current?.InCombat() != true && GeneralHelper.IsItemEffectivelyOwned(item);

    private static bool MayUseOn(GameItem source, GameItem target) =>
        MayUse(source) && target.parentInventory != null && GeneralHelper.IsItemEffectivelyOwned(target);

    // Both ItemMouseDragHandler.EndDrag and ItemSelectHandler use these
    // native GameItem entry points. Match only a source and a bound target.
    [HarmonyPatch]
    private static class TargetChecks
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(GameItem), nameof(GameItem.MayTarget));
            yield return AccessTools.Method(typeof(GameItem), nameof(GameItem.CanTarget));
        }

        private static bool Prefix(GameItem __instance, GameItem? __0, MethodBase __originalMethod, ref bool __result)
        {
            TargetRule? rule = FindRule(__instance, __0);
            if (rule == null)
            {
                return true;
            }

            __result = MayUseOn(__instance, __0!) &&
                (__originalMethod.Name == nameof(GameItem.MayTarget) || rule.CanApply(__instance, __0!));
            return false;
        }
    }

    [HarmonyPatch(typeof(GameItem), nameof(GameItem.Target))]
    private static class TargetAction
    {
        private static bool Prefix(GameItem __instance, GameItem? __0)
        {
            TargetRule? rule = FindRule(__instance, __0);
            if (rule == null)
            {
                return true;
            }

            string sound = __instance.soundUse;
            if (MayUseOn(__instance, __0!) && rule.CanApply(__instance, __0!) && rule.Apply(__instance, __0!))
            {
                AudioManager.Instance?.TryPlayOne(sound);
                OverlayHandler.current?.UpdateInventoryStateUI();
            }
            // The native tool and the mod action must not both consume an item.
            return false;
        }
    }

    [HarmonyPatch]
    private static class FabricSelection
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(GameItem), nameof(GameItem.MaySelectSlotItem));
            yield return AccessTools.Method(typeof(GameItem), nameof(GameItem.CanSelectSlotItem));
        }

        private static bool Prefix(GameItem __instance, ref bool __result)
        {
            if (!Enabled || __instance.identifier != "kotton_fabric")
            {
                return true;
            }
            __result = MayUse(__instance);
            return false;
        }
    }
}
