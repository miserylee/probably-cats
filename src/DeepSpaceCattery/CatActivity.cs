using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace DeepSpaceCattery;

public sealed partial class GameMod
{
    private const string CatNightSaveKey = "cat-night-activity-v1";
    private const string LowerLevel = StoreReputation.FACTION_LOWER_LEVEL;
    private const string Security = StoreReputation.FACTION_SECURITY;
    private const int CatFoodNightLuckBonus = 8;
    private const int DesperateHungerThreshold = 25;
    private const int MaximumNightFactionReputation = 2;

    private enum NightFindKind { Rat, Scrap, Nuts, Electronics, CatFood }

    private sealed class CatNightFind
    {
        public int Day { get; set; }
        public NightFindKind Kind { get; set; }
        public bool RatMale { get; set; }
        public int RatAge { get; set; }
    }

    private sealed class CatNightActivity
    {
        public int LastSettledDay { get; set; } = -1;
        public List<CatNightFind> PendingFinds { get; set; } = new();
        public Dictionary<string, int> LocalAffinity { get; set; } = new();
    }

    private void SettleEmergencyHunger(int day, EmporiumEntry entry, ISet<Cat> outsideForagers, CatNightReport nightReport)
    {
        foreach (Cat cat in cattery!.Cats)
        {
            AgeStage stage = cat.GetStage(day);
            int calories = cat.StoredCalories;
            if (!cat.IsInCare || stage == AgeStage.Dead || cat.PendingBirthPlacement != null ||
                calories > DesperateHungerThreshold * Cattery.KcalPerSatietyPoint)
            {
                continue;
            }
            if (calories == 0)
            {
                RecordStarvationOutside(cat, day, nightReport);
                continue;
            }
            if (stage == AgeStage.Kitten || !catItems.TryGetValue(cat.Id, out GameItem? item) ||
                item.parentInventory is not GameInventory source || !CanRoamFrom(item, source, entry))
            {
                continue;
            }

            Phenotype traits = cat.GetPhenotype();
            double warehouseChance = 22 + .45 * traits.Curiosity + .15 * traits.HuntingDrive;
            if (RollNightChance(warehouseChance) && TryEatWarehouseFood(entry, cat, day, nightReport))
            {
                continue;
            }
            double outsideChance = 30 + .30 * traits.Nocturnality + .15 * traits.Activity - .10 * traits.Vigilance;
            if (!RollNightChance(outsideChance))
            {
                continue;
            }
            outsideForagers.Add(cat);
            if (RollNightRewardChance(cat, day, 20 + .32 * traits.HuntingDrive + .25 * traits.Curiosity))
            {
                AddSatiety(cat, Cattery.DailyFoodKcal);
                nightReport.Add(cat, CatText.T("饿得出门找食物，在外面吃到了一口。"));
            }
            else
            {
                nightReport.Add(cat, CatText.T("饿得出门找食物，空着肚子回来了。"));
            }
        }
    }

    private void SettleCatNightActivity(int day, IReadOnlyDictionary<Cat, GameGridInventory> mealVisitors,
        ISet<Cat> outsideForagers, CatNightReport nightReport)
    {
        if (cattery is not { StarterClaimed: true } state || activeStore == null ||
            EmporiumEntry.Instance is not EmporiumEntry entry ||
            ReadCatNightActivity() is not CatNightActivity activity || day <= activity.LastSettledDay)
        {
            return;
        }

        var reports = new List<(Cat Cat, string Action, string? Faction, int Change)>();
        foreach (Cat cat in state.Cats)
        {
            AgeStage stage = cat.GetStage(day);
            if (!cat.IsInCare || cat.Satiety <= 0 || outsideForagers.Contains(cat) ||
                stage is not (AgeStage.Adult or AgeStage.Senior) ||
                !catItems.TryGetValue(cat.Id, out GameItem? item) ||
                item.parentInventory is not GameInventory location || IsHouseNursery(location) ||
                !GeneralHelper.IsItemOwned(item))
            {
                continue;
            }

            Phenotype traits = cat.GetPhenotype();
            double leaveChance = 5 + .47 * traits.Nocturnality + .1 * traits.Activity + .12 * cat.Satiety -
                .13 * (traits.Vigilance + traits.UnfamiliarNightSoundAvoidanceBonus) -
                (stage == AgeStage.Senior ? 14 : 0);
            if (!RollNightChance(leaveChance))
            {
                continue;
            }

            int huntWeight = 12 + traits.HuntingDrive;
            int scrapWeight = 10 + traits.Curiosity + traits.ScrapHeapSearchBonus;
            int visitWeight = 12 + (traits.Affection + traits.Irritability) / 2;
            int choice = random.Next(huntWeight + scrapWeight + visitWeight);
            string? faction = null;
            int change = 0;
            string action = choice < huntWeight ? HuntAtNight(day, cat, traits, activity)
                : choice < huntWeight + scrapWeight ? ScavengeAtNight(day, cat, traits, activity)
                : VisitAtNight(day, cat, traits, activity, out faction, out change);
            reports.Add((cat, action, faction, change));
        }

        RecordOutings(activeStore, reports, nightReport);
        RoamCatsAtNight(state, day, GetHouseCompartments(entry), mealVisitors, nightReport);
        activity.LastSettledDay = day;
        WriteCatNightActivity(activity);
    }

    private string HuntAtNight(int day, Cat cat, Phenotype traits, CatNightActivity activity)
    {
        if (!RollNightRewardChance(cat, day, 20 + .38 * traits.HuntingDrive + .24 * traits.NightHuntingReaction))
        {
            return CatText.T("在仓储通道追了一阵，空着嘴回了店");
        }
        activity.PendingFinds.Add(new CatNightFind
        {
            Day = day,
            Kind = NightFindKind.Rat,
            RatMale = random.Next(2) == 1,
            RatAge = random.Next(3, 8)
        });
        return CatText.T("在仓储通道捉到一只活老鼠");
    }

    private string ScavengeAtNight(int day, Cat cat, Phenotype traits, CatNightActivity activity)
    {
        if (!RollNightRewardChance(cat, day, 17 + .55 * traits.Curiosity + traits.ScrapHeapSearchBonus))
        {
            return CatText.T("在废料堆里绕了半夜，空手回来了");
        }
        int quality = random.Next(100);
        AddNightFind(activity, day, quality < 10 ? NightFindKind.Electronics :
            quality < 35 ? NightFindKind.Nuts : NightFindKind.Scrap);
        return CatText.T("从废料堆拖回来一件零件");
    }

    private string VisitAtNight(int day, Cat cat, Phenotype traits, CatNightActivity activity,
        out string? affectedFaction, out int change)
    {
        affectedFaction = null;
        change = 0;
        string lowerKey = cat.Id + ":" + LowerLevel;
        string securityKey = cat.Id + ":" + Security;
        int lowerScore = 30 + (traits.Curiosity + traits.HuntingDrive) / 4 +
            (traits.CoatPattern > 1 ? 8 : 0) + ReadLocalAffinity(activity, lowerKey);
        int securityScore = 30 + traits.Affection / 2 - traits.Irritability / 5 +
            (traits.WhiteCoat ? 8 : 0) + ReadLocalAffinity(activity, securityKey);
        bool lower = random.Next(lowerScore + securityScore) < lowerScore;
        string faction = lower ? LowerLevel : Security;
        string key = lower ? lowerKey : securityKey;
        string place = lower ? CatText.T("下层区的爱猫者") : CatText.T("安保岗的爱猫者");
        int affinity = ReadLocalAffinity(activity, key);
        double welcome = (lower ? lowerScore : securityScore) + affinity * 2;
        if (RollNightChance(welcome))
        {
            activity.LocalAffinity[key] = Math.Min(8, affinity + 1);
            affectedFaction = faction;
            change = 1;
            if (RollNightRewardChance(cat, day, 25 + affinity * 3))
            {
                AddNightFind(activity, day, NightFindKind.CatFood);
                return CatText.F("去找{0}串门，带回来一份猫粮", place);
            }
            return CatText.F("去找{0}串门，对方很高兴", place);
        }

        if (RollNightChance(traits.Irritability / 2.0))
        {
            activity.LocalAffinity[key] = Math.Max(-8, affinity - 1);
            affectedFaction = faction;
            change = -1;
            return CatText.F("翻乱了{0}的东西，挨了几句抱怨", place);
        }
        return CatText.T("在住区绕了一圈，自己回来了");
    }

    private static void RecordOutings(PlayerStore store,
        IReadOnlyList<(Cat Cat, string Action, string? Faction, int Change)> reports, CatNightReport nightReport)
    {
        var settled = new Dictionary<string, (string Name, int Requested, double Actual, int Shown)>();
        foreach (var faction in reports.Where(report => report.Faction != null).GroupBy(report => report.Faction!))
        {
            int change = Math.Clamp(faction.Sum(report => report.Change), -MaximumNightFactionReputation, MaximumNightFactionReputation);
            if (change == 0 || store.storeReputations?.Find(entry => entry.factionId == faction.Key) is not StoreReputation reputation)
            {
                continue;
            }

            double before = reputation.GetReputationExact();
            reputation.ModReputation(change);
            double actualChange = Math.Round(reputation.GetReputationExact() - before, 2);
            if (actualChange != 0)
            {
                settled.Add(faction.Key, (reputation.factionDisplay, change, actualChange, 0));
            }
        }
        foreach (var (cat, action, faction, change) in reports)
        {
            string reputationNote = string.Empty;
            if (faction != null && settled.TryGetValue(faction, out var result) &&
                change == Math.Sign(result.Requested) && result.Shown < Math.Abs(result.Requested))
            {
                // Split the settled net delta across the actions that fit the nightly cap.
                int shares = Math.Abs(result.Requested);
                double previous = Math.Round(result.Actual * result.Shown / shares, 2);
                result.Shown++;
                double next = Math.Round(result.Actual * result.Shown / shares, 2);
                double amount = Math.Round(next - previous, 2);
                settled[faction] = result;
                if (amount != 0)
                {
                    reputationNote = CatText.F("，{0}声望 {1:+0.##;-0.##}", result.Name, amount);
                }
            }
            nightReport.Add(cat, CatText.F("{0}{1}。", action, reputationNote));
        }
    }

    private bool RollNightRewardChance(Cat cat, int day, double chance) =>
        RollNightChance(chance + (cat.HasCatFoodBenefits(day) ? CatFoodNightLuckBonus : 0));

    private bool RollNightChance(double chance) => random.NextDouble() * 100 < Math.Clamp(chance, 0, 100);

    private static void AddNightFind(CatNightActivity activity, int day, NightFindKind kind) =>
        activity.PendingFinds.Add(new CatNightFind { Day = day, Kind = kind });

    private static int ReadLocalAffinity(CatNightActivity activity, string key) =>
        activity.LocalAffinity.TryGetValue(key, out int affinity) ? Math.Clamp(affinity, -8, 8) : 0;

    private void DeliverPendingCatNightFinds() => DeliverPendingCatNightFinds(CurrentDay());

    private void DeliverPendingCatNightFinds(int day)
    {
        if (cattery is not { StarterClaimed: true } || EmporiumEntry.Instance is not EmporiumEntry entry ||
            ReadCatNightActivity() is not CatNightActivity activity)
        {
            return;
        }

        if (activity.PendingFinds.RemoveAll(find => find.Day <= day && TryDeliverNightFind(entry, find)) > 0)
        {
            WriteCatNightActivity(activity);
        }
    }

    private bool TryDeliverNightFind(EmporiumEntry entry, CatNightFind find)
    {
        GameItem found = find.Kind == NightFindKind.Rat
            ? PreBuiltItemHelper.CreateRat(find.RatMale ? "MALE" : "FEMALE", Math.Clamp(find.RatAge, 3, 7))
            : ModHelper.SpawnItem(find.Kind switch
            {
                NightFindKind.Scrap => "scrap_metal",
                NightFindKind.Nuts => "nuts_metal",
                NightFindKind.Electronics => "common_electronic",
                NightFindKind.CatFood => CatFoodId,
                _ => "scrap_metal"
            });
        ModHelper.SetItemOwnerShip(found, true);
        found.SetAmount(1);
        if (TryPlaceInVisibleGrid(entry.backInvinvElement, found) || TryPlaceInVisibleGrid(entry.invElement, found))
        {
            return true;
        }
        found.Destroy();
        return false;
    }

    private static bool TryPlaceInVisibleGrid(GameGridInventory? inventory, GameItem item)
    {
        if (inventory == null || inventory.IsInsertLocked())
        {
            return false;
        }

        SlotMarker? slot = inventory.TryFindOneValidInventorySlot(item);
        // Keep the reward visible instead of passing it into a nested container.
        return slot != null && slot.inventory == inventory && slot.TryAcceptOnce(1) == 1;
    }

    private CatNightActivity? ReadCatNightActivity()
    {
        string data = ModHelper.GetModData(modId, CatNightSaveKey, string.Empty);
        if (string.IsNullOrEmpty(data))
        {
            return new CatNightActivity();
        }
        try
        {
            CatNightActivity? activity = JsonConvert.DeserializeObject<CatNightActivity>(data);
            if (activity?.PendingFinds == null || activity.LocalAffinity == null)
            {
                throw new JsonSerializationException("猫咪夜行记录缺少必要字段。");
            }
            return activity;
        }
        catch (JsonException error)
        {
            log.Error(error);
            return null;
        }
    }

    private void WriteCatNightActivity(CatNightActivity activity) =>
        ModHelper.SetModData(modId, CatNightSaveKey, JsonConvert.SerializeObject(activity));
}
