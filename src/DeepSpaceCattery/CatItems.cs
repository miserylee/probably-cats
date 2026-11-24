using System.Collections.Generic;
using System.Linq;

namespace DeepSpaceCattery;

public sealed partial class GameMod
{
    private const string ProtectedFromSale = "NOT_FOR_RESALE";
    private static string HouseDescription => CatText.T("供猫栖息的窝舍，可安装牲畜饲料分配器。同住的成年猫有机会交配，加建繁育间可提高受孕几率。");
    private readonly Dictionary<string, GameItem> catItems = new();
    private CatPortraits? portraits;

    private string HouseId => modId + ".cattery_house_v2";
    private string PreviousHouseId => modId + ".cattery_house";
    private string CatIdTag => modId + ".cat_id";
    private string AdultCatId => modId + ".cat_adult";
    private string KittenId => modId + ".cat_kitten";

    private GameItem CreateHouse(bool legacy, bool previous = false)
    {
        GameItem item = ItemDirectory.CreateEmptyItem(legacy ? PortableCatteryId : previous ? PreviousHouseId : HouseId);
        item.name = legacy ? CatText.T("猫舍旧终端") : CatText.T("猫舍");
        item.shortDescription = legacy
            ? CatText.F("旧式猫舍终端。腾出 {0}×{1} 格空间后，双击可换成猫舍。", HouseWidth, HouseHeight)
            : HouseDescription;
        item.useSpriteFromMod = true;
        item.SetSprite(modId, legacy ? "portable_cattery" : "cattery_house");
        item.SetShape(new GridShapeBuilder().SetDataFill(legacy ? 1 : previous ? 3 : HouseWidth,
            legacy ? 1 : previous ? 2 : HouseHeight).Build());
        item.SetValue(CatteryPrices.House);
        item.onLoaded += house => house.SetValue(CatteryPrices.House);
        ProtectItem(item);
        if (legacy)
        {
            item.activateDefault = true;
            item.mayActivateSlotItemFunc = (_, _, _) => enabled;
            item.canActivateSlotItemFunc = (_, _, _) => HasCattery();
            item.onActivateSlotItemFunc = (_, _, _) => RestoreCatItems();
        }
        else
        {
            item.onLoaded += house => house.shortDescription = HouseDescription;
            AttachHouseInventory(item);
        }
        return item;
    }

    private GameItem CreateCatItem(bool kitten)
    {
        GameItem item = ItemDirectory.CreateEmptyItem(kitten ? KittenId : AdultCatId);
        item.name = CatText.T("猫咪");
        item.shortDescription = kitten ? CatText.T("幼猫还不会独自出门。") : CatText.T("夜里偶尔会出门捕猎或拾荒。");
        item.useSpriteFromMod = true;
        item.SetSprite(modId, kitten ? "cattery_kitten_item" : "cattery_adult_item");
        item.SetShape(new GridShapeBuilder().SetDataFill(2, kitten ? 1 : 4).Build());
        ProtectItem(item);
        item.onLoaded += ClearCatBuyerBuff;
        var mayStore = item.mayInventoryAddItemFunc;
        item.mayInventoryAddItemFunc = (cat, inventory) =>
            (enabled && inventory == EmporiumEntry.Instance?.trashcanInvElement && GeneralHelper.IsItemOwned(cat)) ||
            mayStore(cat, inventory);
        GameInventory? previousInventory = null;
        item.onSlotRemoveItemFunc += (_, inventory, _) => previousInventory = inventory;
        item.onSlotAddItemFunc += (cat, inventory, _) =>
        {
            GameInventory? source = previousInventory;
            previousInventory = null;
            OnCatItemPlaced(cat, inventory, source);
        };
        ConfigureCatToolTargets(item);
        return item;
    }

    private static void ProtectItem(GameItem item)
    {
        item.EnableTag(GeneralHelper.IMPORTANT_TAG);
        item.EnableTag(ProtectedFromSale);
        item.mayStackItemFunc = (_, _) => false;
        item.maxSlotAmountItemFunc = (_, _, _) => 1;
        item.mayInventoryAddItemFunc = (_, inventory) =>
        {
            EmporiumEntry entry = EmporiumEntry.Instance;
            return entry == null || (inventory != entry.trashInvElement && inventory != entry.trashcanInvElement &&
                inventory != entry.drainInvElement && inventory != entry.soldElement);
        };
    }

    private string ReadCatId(GameItem item) => item.GetTagReadonly(CatIdTag).GetString();

    private void RestoreCatItems() => RestoreCatItems(recoverMissing: false);

    private void RestoreLoadedCatItems()
    {
        RegisterCatLoversService();
        RestorePetShopDeliveries();
        RestoreCatItems(recoverMissing: true);
        DeliverPendingCatNightFinds();
    }

    private void RestoreCatItems(bool recoverMissing)
    {
        if (!HasCattery())
        {
            return;
        }
        bool changed = SynchronizeCatItems(CurrentDay(), recoverMissing: recoverMissing);
        changed |= RestoreCatFood();
        changed |= DropPendingCatFur();
        changed |= RestoreCleanupFood();
        changed |= RestoreLooseCatSupplyItems();
        if (changed)
        {
            SaveCattery();
        }
    }

    private bool SynchronizeCatItems(int day, IReadOnlyList<Cat>? newborns = null, bool recoverMissing = false)
    {
        Cattery state = cattery!;
        EmporiumEntry entry = EmporiumEntry.Instance;
        if (!state.StarterClaimed || entry == null)
        {
            return false;
        }

        bool discarded = false;
        foreach (GameItem item in GetTrashContents(entry).Where(IsCatItem).ToArray())
        {
            discarded |= AbandonCat(item);
        }
        IReadOnlyList<GameItem> owned = entry.GetAllOwnedItems().Distinct().ToArray();
        GameGridInventory[] destinations = { entry.invElement, entry.backInvinvElement, entry.showcaseElement,
            entry.frontInvinvElement, entry.backInvinvElementCounter };
        GameItem? house = SynchronizeHouse(state, owned, destinations, out bool changed);
        if (state.StarterFeederPending && house != null && InstallStarterFeeder(house))
        {
            state.StarterFeederPending = false;
            changed = true;
        }
        changed |= discarded;
        GameGridInventory? houseInventory = house == null ? null : MainCompartment(house);
        GameItem[] houses = FindHouses(entry);
        bool populateHouse = houseInventory != null && !state.HouseResidentsInitialized;
        ILookup<string, GameItem> existing = owned.Where(IsCatItem).ToLookup(ReadCatId);
        var pendingDeliveryIds = recoverMissing ? PendingPetShopCatIds() : null;
        // Snapshot the mother's location before dead cats are removed or growing cats change items.
        var birthLocations = state.Cats.Where(cat => cat.IsInCare && cat.PendingBirthPlacement == null && !existing.Contains(cat.Id) &&
                (!cat.HasPlacedItem || recoverMissing))
            .Select(cat => cat.MotherId).OfType<string>().Distinct()
            .ToDictionary(id => id, id => FindBirthLocation(id, state, existing, houses));
        catItems.Clear();
        var records = state.Cats.Where(cat => cat.IsInCare).Select(cat => (Cat: cat, Stage: cat.GetStage(day))).ToArray();
        foreach (var record in records.Where(record => record.Stage == AgeStage.Dead))
        {
            Cat cat = record.Cat;
            if (cat.DeathCause == CatDeathCause.Starvation)
            {
                foreach (GameItem stale in existing[cat.Id])
                {
                    changed |= ReplaceDeadCatItem(stale, cat);
                }
                continue;
            }
            GameItem? item = existing[cat.Id].FirstOrDefault();
            if (item != null)
            {
                changed |= ReplaceDeadCatItem(item, cat);
            }
        }

        var newbornIds = newborns is { Count: > 0 } ? new HashSet<string>(newborns.Select(cat => cat.Id)) : null;
        foreach (var record in records.Where(record => record.Stage != AgeStage.Dead)
            .OrderBy(record => newbornIds?.Contains(record.Cat.Id) == true ? 0 : 1))
        {
            Cat cat = record.Cat;
            AgeStage stage = record.Stage;
            GameItem[] found = existing[cat.Id].ToArray();
            bool kitten = stage == AgeStage.Kitten;
            string identifier = kitten ? KittenId : AdultCatId;
            GameItem? item = found.FirstOrDefault(candidate => candidate.identifier == identifier);
            GameItem? beforeGrowth = found.FirstOrDefault();
            if (beforeGrowth != null)
            {
                changed |= ImportLegacyCatName(beforeGrowth, cat);
            }
            if (item == null && (beforeGrowth != null || ((houseInventory != null || cat.MotherId != null) &&
                (!cat.HasPlacedItem || recoverMissing) && pendingDeliveryIds?.Contains(cat.Id) != true)))
            {
                var replacement = ModHelper.SetItemOwnerShip(ModHelper.SpawnItem(identifier), true);
                replacement.ModifyTag(CatIdTag, tag => tag.SetString(cat.Id));
                if (beforeGrowth == null)
                {
                    if (cat.MotherId is string motherId)
                    {
                        CatBirthPlacement placement = cat.PendingBirthPlacement ?? birthLocations[motherId];
                        if (TryPlaceNewborn(placement, replacement, entry, houses))
                        {
                            item = replacement;
                        }
                        else
                        {
                            replacement.Destroy();
                            if (newbornIds?.Contains(cat.Id) == true)
                            {
                                cat.WasAbandoned = true;
                                cat.PendingBirthPlacement = null;
                                changed = true;
                            }
                            else
                            {
                                changed |= cat.PendingBirthPlacement == null || cat.HasPlacedItem;
                                cat.PendingBirthPlacement = placement;
                            }
                            cat.HasPlacedItem = false;
                        }
                    }
                    else
                    {
                        GraphUtils.EmporiumTryAdd(houseInventory!, replacement);
                        item = replacement.parentInventory != null ? replacement : null;
                    }
                }
                else if (TryGrowInHouse(beforeGrowth, replacement, beforeGrowth.parentInventory as GameGridInventory) ||
                    TryPlace(replacement, beforeGrowth.parentInventory as GameGridInventory, destinations))
                {
                    item = replacement;
                }
                item ??= beforeGrowth;
            }

            if (item == null)
            {
                continue;
            }
            if (cat.PendingBirthPlacement != null)
            {
                cat.PendingBirthPlacement = null;
                changed = true;
            }
            if (populateHouse && cat.MotherId == null && found.Contains(item) &&
                !houses.Any(home => item.parentInventory == MainCompartment(home) ||
                    item.parentInventory == NurseryCompartment(home)))
            {
                TryPlaceIn(item, houseInventory);
            }
            foreach (GameItem duplicate in found.Where(candidate => !ReferenceEquals(candidate, item)))
            {
                duplicate.Destroy();
            }
            if (!cat.HasPlacedItem)
            {
                cat.HasPlacedItem = true;
                changed = true;
            }
            catItems.Add(cat.Id, item);
            UpdateCatItem(item, cat, stage);
        }
        if (populateHouse)
        {
            state.HouseResidentsInitialized = true;
            changed = true;
        }
        return changed;
    }

    private void RememberBirthPlacements(IReadOnlyList<Cat> newborns)
    {
        if (newborns.Count == 0 || EmporiumEntry.Instance is not EmporiumEntry entry)
        {
            return;
        }

        GameItem[] houses = FindHouses(entry);
        ILookup<string, GameItem> existing = entry.GetAllOwnedItems().Where(IsCatItem).ToLookup(ReadCatId);
        foreach (var litter in newborns.Where(cat => cat.MotherId != null && cat.PendingBirthPlacement == null)
            .GroupBy(cat => cat.MotherId!))
        {
            CatBirthPlacement placement = FindBirthLocation(litter.Key, cattery!, existing, houses);
            foreach (Cat kitten in litter)
            {
                kitten.PendingBirthPlacement = placement;
            }
        }
    }

    private CatBirthPlacement FindBirthLocation(
        string motherId, Cattery state, ILookup<string, GameItem> existing, GameItem[] houses)
    {
        var motherLocations = existing[motherId].Select(item => item.parentInventory).OfType<GameInventory>().ToHashSet();
        foreach (GameItem house in houses)
        {
            GameGridInventory? nursery = NurseryCompartment(house);
            if (nursery != null && motherLocations.Contains(nursery))
            {
                return new CatBirthPlacement { HouseId = GetHouseIdentity(house), InNursery = true };
            }
            if (MainCompartment(house) is GameGridInventory main && motherLocations.Contains(main))
            {
                return new CatBirthPlacement { HouseId = GetHouseIdentity(house) };
            }
        }

        Cat? mother = state.Cats.Find(cat => cat.Id == motherId);
        string? legacyHouseId = houses.Length == 0 ? null : GetHouseIdentity(houses[0]);
        return new CatBirthPlacement { HouseId = mother == null ? null : GetRoamingHouseId(mother, legacyHouseId) };
    }

    private bool TryPlaceNewborn(CatBirthPlacement placement, GameItem item, EmporiumEntry entry, GameItem[] houses)
    {
        GameItem? home = placement.HouseId == null ? null :
            houses.FirstOrDefault(house => GetHouseIdentity(house) == placement.HouseId);
        GameGridInventory? main = home == null ? null : MainCompartment(home);
        GameGridInventory? nursery = home == null ? null : NurseryCompartment(home);
        return TryPlaceInDirectGrid(placement.InNursery ? nursery : main, item) ||
            TryPlaceInDirectGrid(placement.InNursery ? main : nursery, item) ||
            TryPlaceInDirectGrid(entry.backInvinvElement, item);
    }

    private static bool TryGrowInHouse(GameItem kitten, GameItem adult, GameGridInventory? house)
    {
        if (house == null || kitten.parentInventory != house)
        {
            return false;
        }
        GridShape oldShape = kitten.modifiedShape.Clone();
        if (!house.Expel(kitten))
        {
            return false;
        }
        if (TryPlaceIn(adult, house))
        {
            return true;
        }
        // The original grid and position were untouched while the kitten was out.
        kitten.modifiedShape = oldShape;
        house.UncheckedAccept(kitten);
        return false;
    }

    private GameItem? SynchronizeHouse(Cattery state, IReadOnlyList<GameItem> owned, GameGridInventory[] destinations,
        out bool changed)
    {
        changed = false;
        GameItem? house = owned.FirstOrDefault(item => item.identifier == HouseId);
        GameItem? previous = owned.FirstOrDefault(item => item.identifier == PreviousHouseId);
        if (house == null && (previous != null || (!state.HasPlacedHouse && !HasPendingPetShopHouse())))
        {
            GameItem newHouse = ModHelper.SetItemOwnerShip(ModHelper.SpawnItem(HouseId), true);
            if (previous != null)
            {
                CopyHouseLayout(previous, newHouse);
            }
            GameInventory? formerParent = previous?.parentInventory;
            GridShape? formerShape = previous?.modifiedShape.Clone();
            bool detached = previous != null && formerParent?.Expel(previous) == true;
            if (previous != null && !detached)
            {
                newHouse.Destroy();
            }
            else if (TryPlace(newHouse, formerParent as GameGridInventory, destinations))
            {
                if (previous == null || TryMoveHouseContents(previous, newHouse))
                {
                    house = newHouse;
                    previous?.Destroy();
                    changed = true;
                }
                else
                {
                    newHouse.Destroy();
                }
            }
            if (house == null && detached)
            {
                previous!.modifiedShape = formerShape!;
                formerParent!.UncheckedAccept(previous);
            }
        }
        house ??= previous;
        if (house == null)
        {
            if (!state.HasPlacedHouse && !owned.Any(item => item.identifier == PortableCatteryId))
            {
                var marker = ModHelper.SetItemOwnerShip(ModHelper.SpawnItem(PortableCatteryId), true);
                TryPlace(marker, null, destinations);
            }
            return null;
        }

        foreach (GameItem legacy in owned.Where(item => item.identifier == PortableCatteryId))
        {
            legacy.Destroy();
        }
        changed |= !state.HasPlacedHouse;
        state.HasPlacedHouse = true;
        return house;
    }

    private static bool TryMoveHouseContents(GameItem fromHouse, GameItem toHouse)
    {
        var relocated = new List<(GameInventory From, GameInventory To, GameItem Item, GridShape Shape)>();
        for (int index = 0; index <= 4; index++)
        {
            var from = MachineHelper.GetBodyStorage(fromHouse, index) as GameInventory;
            var to = MachineHelper.GetBodyStorage(toHouse, index) as GameInventory;
            if (from == null || to == null)
            {
                continue;
            }
            foreach (GameItem item in from.childItems.ToArray())
            {
                GridShape shape = item.modifiedShape.Clone();
                if (!from.Expel(item))
                {
                    UndoMoves();
                    return false;
                }
                item.modifiedShape = shape.Clone();
                relocated.Add((from, to, item, shape));
                if (!to.UncheckedAccept(item))
                {
                    UndoMoves();
                    return false;
                }
            }
        }
        toHouse.contentWindow?.Validate();
        return true;

        void UndoMoves()
        {
            for (int index = relocated.Count - 1; index >= 0; index--)
            {
                var (from, to, item, shape) = relocated[index];
                if (ReferenceEquals(item.parentInventory, to))
                {
                    to.Expel(item);
                }
                item.modifiedShape = shape;
                from.UncheckedAccept(item);
            }
        }
    }

    private static bool TryPlace(GameItem item, GameGridInventory? preferred, IEnumerable<GameGridInventory> destinations)
    {
        if (TryPlaceIn(item, preferred))
        {
            return true;
        }
        foreach (GameGridInventory inventory in destinations)
        {
            if (inventory != preferred && TryPlaceIn(item, inventory))
            {
                return true;
            }
        }
        item.Destroy();
        return false;
    }

    private static bool TryPlaceIn(GameItem item, GameGridInventory? inventory)
    {
        if (inventory == null)
        {
            return false;
        }
        SlotMarker? slot = inventory.TryFindOneValidInventorySlot(item, keepOrientation: true);
        return slot?.TryAcceptOnce(1) == 1 && item.parentInventory != null;
    }

    private void UpdateCatItem(GameItem item, Cat cat, AgeStage stage, CatPortraits? stockPortraits = null)
    {
        SetCatMarketTraits(item, cat, stage);
        item.name = cat.Name;
        string habits = stage == AgeStage.Kitten ? CatText.T("还不会独自出门。") : CatText.T("夜里偶尔会出门捕猎或拾荒。");
        string details = stockPortraits == null ? CatText.T("双击可查看三代血亲。") : string.Empty;
        string animal = cat.IsMale ? CatText.T("公猫") : CatText.T("母猫");
        string age = stage switch { AgeStage.Kitten => CatText.T("年幼"), AgeStage.Senior => CatText.T("年迈"), _ => CatText.T("成年") };
        item.shortDescription = CatText.F("一只{0}的{1}，{2}{3}\n{4}", age, animal, habits, details, DescribeAppearance(cat.GetPhenotype()));
        AgeStage itemStage = item.identifier == KittenId ? AgeStage.Kitten : AgeStage.Adult;
        string key = (stockPortraits ?? portraits)?.RegisterItemSprite(cat, itemStage) ??
            (itemStage == AgeStage.Kitten ? "cattery_kitten_item" : "cattery_adult_item");
        item.useSpriteFromMod = true;
        item.SetSpriteQuick(modId, key);
    }

    private string CatLocation(Cat cat, AgeStage stage)
    {
        if (!catItems.TryGetValue(cat.Id, out GameItem? item))
        {
            return cat.HasPlacedItem ? CatText.T("位置暂时不可用。") : CatText.T("店内空间不足。腾出空位后再打开猫舍。");
        }
        EmporiumEntry entry = EmporiumEntry.Instance;
        GameInventory? inventory = item.parentInventory;
        string place = inventory == entry.backInvinvElement ? CatText.T("后区收纳") :
            inventory == entry.invElement ? CatText.T("背包") :
            inventory == entry.showcaseElement ? CatText.T("展示柜") :
            inventory == entry.frontInvinvElement ? CatText.T("前柜台") :
            inventory == entry.backInvinvElementCounter ? CatText.T("后柜台") :
            inventory?.GetParentItem()?.name ?? CatText.T("店内收纳");
        bool growing = item.identifier == KittenId && stage != AgeStage.Kitten;
        return growing ? CatText.F("位置 · {0}。腾出 2×4 格后会换为成猫立绘。", place) : CatText.F("位置 · {0}", place);
    }
}
