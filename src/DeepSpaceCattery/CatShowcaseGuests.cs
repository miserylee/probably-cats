using System.Linq;
using HarmonyLib;

namespace DeepSpaceCattery;

public sealed partial class GameMod
{
    private const int ShowcaseOrdinaryVisitorChance = 20;
    private const int ShowcaseOrdinaryVisitorDraws = 20;

    [HarmonyPatch(typeof(ClienteleUIManager), nameof(ClienteleUIManager.RefreshUI))]
    private static class ShowCatShowcaseForecast
    {
        private static void Postfix(ClienteleUIManager __instance)
        {
            if (__instance.panel?.activeSelf != true || __instance.merchandiseCustomerTMP == null ||
                petShopTradeOwner?.TryGetCatShowcaseForecast(out bool ordinaryVisitor) != true)
            {
                return;
            }

            var label = __instance.merchandiseCustomerTMP;
            string current = label.text?.TrimEnd() ?? string.Empty;
            string? empty = LocHelper.GetLocalizedUI("ui_clientele_no_merch_detected")?.TrimEnd();
            string forecast = CatText.T("- 猫咪爱好者 (猫咪)");
            label.text = current.Length == 0 || current == empty ? forecast : current + "\n" + forecast;

            if (ordinaryVisitor && __instance.normalCustomerTMP is { } normalLabel)
            {
                string normal = normalLabel.text?.TrimEnd() ?? string.Empty;
                string visitor = LocHelper.GetLocalizedUI("ui_clientele_random_customer");
                string row = CatText.F("- 猫咪: {0} ({1}%)", visitor, ShowcaseOrdinaryVisitorChance);
                normalLabel.text = normal.Length == 0 ? row : normal + "\n" + row;
            }
        }
    }

    private bool TryGetCatShowcaseForecast(out bool ordinaryVisitor)
    {
        ordinaryVisitor = false;
        if (!enabled || cattery == null || activeStore is not PlayerStore store ||
            !ReferenceEquals(store, PlayerStore.Instance))
        {
            return false;
        }

        int day = CurrentDay();
        if (!FindCatSaleCandidates().Any(offer => IsShowcaseCat(offer.Cat, offer.Item, day)))
        {
            return false;
        }

        ordinaryVisitor = CanAttractOrdinaryShowcaseVisitor(store, day);
        return true;
    }

    private static bool CanAttractOrdinaryShowcaseVisitor(PlayerStore store, int day) =>
        day > (store.skipIntro ? 1 : StoreClientManager.DAY_UNTIL_NORMALE_CLIENT_SPAWN);

    private void QueueShowcaseOrdinaryVisitor(StoreClientManager manager)
    {
        if (activeStore is not PlayerStore store ||
            !CanAttractOrdinaryShowcaseVisitor(store, CurrentDay()) ||
            random.Next(100) >= ShowcaseOrdinaryVisitorChance)
        {
            return;
        }

        StoreClient.ClientIntent preferredIntent = random.Next(2) == 0
            ? StoreClient.ClientIntent.BUY : StoreClient.ClientIntent.SELL;
        string faction = manager.ComputeClientType();
        StoreClient? visitor = faction == StoreReputation.FACTION_BLACK_MARKET ? null
            : PickShowcaseOrdinaryVisitor(manager, faction, preferredIntent);
        if (visitor == null && faction != StoreReputation.FACTION_LOWER_LEVEL)
        {
            visitor = PickShowcaseOrdinaryVisitor(manager, StoreReputation.FACTION_LOWER_LEVEL, preferredIntent);
        }
        if (visitor != null)
        {
            // Duplicates have already been filtered, including if the native queue contains null slots.
            manager.TryAddClient(visitor, false);
        }
    }

    private static StoreClient? PickShowcaseOrdinaryVisitor(StoreClientManager manager, string faction,
        StoreClient.ClientIntent preferredIntent)
    {
        StoreClient? fallback = null;
        for (int draw = 0; draw < ShowcaseOrdinaryVisitorDraws; draw++)
        {
            StoreClient? candidate = manager.PickRandomClient(faction);
            if (candidate == null ||
                candidate.clientIntent is not (StoreClient.ClientIntent.BUY or StoreClient.ClientIntent.SELL) ||
                candidate.isSecurity || candidate.isWanted ||
                !StoreClientManager.IsClientValid(candidate) || StoreClientManager.IsClientBlockedByKnownSnitch(candidate) ||
                manager.clientStack.Any(queued => queued?.identifier == candidate.identifier))
            {
                continue;
            }
            if (candidate.clientIntent == preferredIntent)
            {
                return candidate;
            }
            fallback ??= candidate;
        }
        return fallback;
    }
}
