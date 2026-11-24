using System.Collections.Generic;
using System.Linq;

namespace DeepSpaceCattery;

public sealed partial class GameMod
{
    private GameItem? movingRoamingCat;

    private void RoamCatsAtNight(Cattery state, int day, HouseCompartments[] houses,
        IReadOnlyDictionary<Cat, GameGridInventory> mealVisitors, CatNightReport nightReport)
    {
        if (EmporiumEntry.Instance is not EmporiumEntry entry)
        {
            return;
        }

        var shelters = new Dictionary<GameGridInventory, string>();
        var nests = houses.ToDictionary(house => house.Nest);
        foreach (HouseCompartments house in houses)
        {
            AddShelter(house.Nest, CatText.T("猫窝"));
        }
        AddShelter(entry.invElement, CatText.T("店内收纳"));
        AddShelter(entry.backInvinvElement, CatText.T("后区收纳"));
        AddShelter(entry.showcaseElement, CatText.T("展柜"));
        foreach (GameItem container in entry.GetAllOwnedItems(includeAfterhour: false))
        {
            if (IsHouse(container) || !container.IsTag("CONTAINER_TAG") ||
                GeneralHelper.IsItemLocked(container) || container.contentWindow?.childElement is not PixelElement contents)
            {
                continue;
            }
            foreach (GameGridInventory inventory in MachineryContainerUtils.Collect(contents).OfType<GameGridInventory>())
            {
                AddShelter(inventory, container.GetDisplayName(preferCustomName: true));
            }
        }

        foreach (Cat cat in state.Cats)
        {
            AgeStage stage = cat.GetStage(day);
            if (!cat.IsInCare || stage is not (AgeStage.Adult or AgeStage.Senior) ||
                !catItems.TryGetValue(cat.Id, out GameItem? item) || item.parentInventory is not GameInventory source ||
                !CanRoamFrom(item, source, entry))
            {
                continue;
            }

            if (mealVisitors.TryGetValue(cat, out GameGridInventory? mealNest))
            {
                // A cat can eat at the entrance even when the nest has no space for its item.
                if (source != mealNest && !mealNest.IsInsertLocked() && TryRoamInto(item, source, mealNest))
                {
                    ClearRoamingHouse(cat);
                    nightReport.Add(cat, CatText.T("回猫窝吃了东西。"));
                }
                cat.HasPlacedItem = item.parentInventory != null;
                continue;
            }

            Phenotype traits = cat.GetPhenotype();
            double chance = 12 + .30 * traits.Activity + .15 * traits.Curiosity + .12 * traits.Nocturnality -
                (stage == AgeStage.Senior ? 10 : 0);
            if (!RollNightChance(chance))
            {
                continue;
            }
            HouseCompartments? origin = source is GameGridInventory sourceGrid ? nests.GetValueOrDefault(sourceGrid) : null;
            string? homeId = origin?.Id ?? GetRoamingHouseId(cat, houses.FirstOrDefault()?.Id);
            GameGridInventory? home = houses.FirstOrDefault(house => house.Id == homeId)?.Nest;
            GameGridInventory? preferredNest = home ?? houses.FirstOrDefault()?.Nest;
            bool tryHomeFirst = preferredNest != null && source != preferredNest && RollNightChance(cat.Satiety < 45 ? 65 : 30);
            var choices = shelters.Where(pair => pair.Key != source &&
                (home == null || pair.Key == home || !nests.ContainsKey(pair.Key)))
                .OrderBy(pair => tryHomeFirst && pair.Key == preferredNest ? -1 : random.Next());
            foreach (var (destination, label) in choices)
            {
                if (TryRoamInto(item, source, destination))
                {
                    if (nests.ContainsKey(destination))
                    {
                        ClearRoamingHouse(cat);
                    }
                    else if (origin != null)
                    {
                        ClearRoamingHouse(cat);
                        cat.RoamingHouseId = origin.Id;
                    }
                    nightReport.Add(cat, CatText.F("钻进了「{0}」。", label));
                    break;
                }
                if (item.parentInventory != source)
                {
                    cat.HasPlacedItem = item.parentInventory != null;
                    break;
                }
            }
        }

        void AddShelter(GameGridInventory? inventory, string label)
        {
            if (inventory != null && !IsHouseNursery(inventory) && inventory != entry.trashcanInvElement &&
                inventory != entry.soldElement && !inventory.IsInsertLocked())
            {
                shelters.TryAdd(inventory, label);
            }
        }
    }

    private bool IsHouseNursery(GameInventory source) => source.GetParentItem() is GameItem house && IsHouse(house) &&
        ReferenceEquals(source, NurseryCompartment(house));

    private bool CanRoamFrom(GameItem item, GameInventory source, EmporiumEntry entry) =>
        !IsHouseNursery(source) && item.MayRemove() && GeneralHelper.IsItemEffectivelyOwned(item) &&
        IsAccessibleStoreInventory(entry, source);

    private static string? GetRoamingHouseId(Cat cat, string? legacyHouseId)
    {
        if (cat.RoamingFromMainCompartment && legacyHouseId != null)
        {
            cat.RoamingHouseId ??= legacyHouseId;
            cat.RoamingFromMainCompartment = false;
        }
        return cat.RoamingHouseId;
    }

    private static void ClearRoamingHouse(Cat cat)
    {
        cat.RoamingHouseId = null;
        cat.RoamingFromMainCompartment = false;
    }

    private bool TryRoamInto(GameItem item, GameInventory source, GameGridInventory destination)
    {
        SlotMarker? slot = destination.TryFindOneValidInventorySlot(item, keepOrientation: true);
        if (slot?.inventory != destination || slot.targetItem != null)
        {
            return false;
        }
        GridShape previous = item.modifiedShape.Clone();
        GameItem? previousMovingCat = movingRoamingCat;
        movingRoamingCat = item;
        try
        {
            if (slot.TryAcceptOnce(1) == 1 && item.parentInventory == destination)
            {
                return true;
            }
            if (item.parentInventory == null)
            {
                item.modifiedShape = previous;
                if (!source.UncheckedAccept(item))
                {
                    GraphUtils.EmporiumTryAdd(source, item);
                }
            }
            return false;
        }
        finally
        {
            movingRoamingCat = previousMovingCat;
        }
    }
}
