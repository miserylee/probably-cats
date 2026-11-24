using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace DeepSpaceCattery;

public sealed partial class GameMod
{
    private const string NeuteringToolId = "wire_cutter";
    private readonly HashSet<string> cleanupCorpses = new();

    private string CatCorpseId => modId + ".cat_corpse";

    private void RegisterLifecycleItems(ModItemDirectory directory) => directory.Add(CatCorpseId, CreateCatCorpse);

    private GameItem CreateCatCorpse()
    {
        GameItem item = ItemDirectory.CreateEmptyItem(CatCorpseId);
        item.name = CatText.T("猫尸");
        item.shortDescription = CatText.T("可以丢进垃圾桶。能与黑市交易时，夜间清洁服务每收走一具猫尸，就会留下一份猫粮。");
        item.useSpriteFromMod = true;
        item.SetSprite(modId, "cattery_kitten_item");
        item.SetShape(new GridShapeBuilder().SetDataFill(2, 1).Build());
        item.EnableTag(ProtectedFromSale);
        item.unitValue = CatteryPrices.Corpse;
        item.mayStackItemFunc = (_, _) => false;
        item.maxSlotAmountItemFunc = (_, _, _) => 1;
        item.mayInventoryAddItemFunc = (_, inventory) => inventory != EmporiumEntry.Instance?.soldElement;
        return item;
    }

    private void ConfigureCatToolTargets(GameItem item)
    {
        NativeCatInteraction.Bind(item, IsCatTool, (source, target) => GetToolTargetCat(source, target) != null,
            ApplyCatTool);
    }

    private bool ApplyCatTool(GameItem source, GameItem target)
    {
        Cat? cat = GetToolTargetCat(source, target);
        if (cat == null)
        {
            return false;
        }

        if (source.identifier == CatFoodId)
        {
            if (!FeedCatFood(cat, source, CurrentDay() + 1))
            {
                return false;
            }
        }
        else
        {
            cat.Neutered = true;
            cat.NeuteredDay = CurrentDay();
        }
        SetCatMarketTraits(target, cat, cat.GetStage(CurrentDay()));
        SaveCattery();
        return true;
    }

    private bool IsCatTool(GameItem source) => source.identifier == CatFoodId || source.identifier == NeuteringToolId;

    private Cat? GetToolTargetCat(GameItem source, GameItem target)
    {
        if (!enabled || !IsCatTool(source) || !HasCattery())
        {
            return null;
        }

        string id = ReadCatId(target);
        Cat? cat = cattery!.Cats.Find(candidate => candidate.Id == id);
        if (cat == null || !cat.IsInCare)
        {
            return null;
        }

        AgeStage stage = cat.GetStage(CurrentDay());
        if (stage == AgeStage.Dead ||
            source.identifier == CatFoodId && cat.StoredCalories >= Cattery.MaximumSatietyKcal ||
            source.identifier == NeuteringToolId && (cat.Neutered || stage == AgeStage.Kitten))
        {
            return null;
        }
        return cat;
    }

    private void OnCatItemPlaced(GameItem item, GameInventory inventory, GameInventory? previousInventory)
    {
        if (!enabled || EmporiumEntry.Instance is not EmporiumEntry entry)
        {
            return;
        }
        if (inventory == entry.trashcanInvElement)
        {
            entry.StartCoroutine(AbandonAfterDrop(item, entry));
            return;
        }

        // Reordering within a container or loading a fresh instance doesn't change the cat's group.
        string id = ReadCatId(item);
        if (previousInventory != null && previousInventory != inventory && item != movingRoamingCat &&
            !string.IsNullOrEmpty(id) &&
            catItems.TryGetValue(id, out GameItem? tracked) && tracked == item &&
            cattery?.Cats.Find(cat => cat.Id == id) is Cat cat &&
            (cat.RoamingHouseId != null || cat.RoamingFromMainCompartment))
        {
            ClearRoamingHouse(cat);
            SaveCattery();
        }
    }

    private IEnumerator AbandonAfterDrop(GameItem item, EmporiumEntry entry)
    {
        // Finish the native placement and sprite update before removing the item.
        yield return null;
        if (enabled && EmporiumEntry.Instance == entry && item.parentInventory == entry.trashcanInvElement &&
            HasCattery() && AbandonCat(item))
        {
            SaveCattery();
        }
    }

    private bool AbandonCat(GameItem item)
    {
        string id = ReadCatId(item);
        Cat? cat = cattery!.Cats.Find(candidate => candidate.Id == id);
        if (cat == null || cat.WasSold || cat.GetStage(CurrentDay()) == AgeStage.Dead)
        {
            return false;
        }

        cat.WasAbandoned = true;
        cattery.BreedingJobs.RemoveAll(job => job.MotherId == cat.Id);
        catItems.Remove(cat.Id);
        catsForCurrentBuyer.Remove(item);
        item.Destroy();
        return true;
    }

    private void RecordStarvationOutside(Cat cat, int day, CatNightReport nightReport)
    {
        if (!cat.Alive || !cat.IsInCare || cat.StoredCalories > 0)
        {
            return;
        }

        cat.RecordDeath(CatDeathCause.Starvation, day);
        ClearRoamingHouse(cat);
        cat.HasPlacedItem = false;
        cattery!.BreedingJobs.RemoveAll(job => job.MotherId == cat.Id);
        nightReport.Add(cat, CatText.T("夜里跑出去找食物，饿死在了外面。"));
    }

    private bool ReplaceDeadCatItem(GameItem item, Cat cat)
    {
        if (cat.DeathCause == CatDeathCause.Starvation)
        {
            catItems.Remove(cat.Id);
            catsForCurrentBuyer.Remove(item);
            cat.HasPlacedItem = false;
            item.Destroy();
            return true;
        }

        bool recorded = cat.DeathCause == CatDeathCause.None;
        cat.RecordDeath(CatDeathCause.Natural, CurrentDay());
        if (cat.CorpsePlaced)
        {
            item.Destroy();
            catItems.Remove(cat.Id);
            return true;
        }

        GameItem corpse = ModHelper.SetItemOwnerShip(ModHelper.SpawnItem(CatCorpseId), true);
        corpse.ModifyTag(CatIdTag, tag => tag.SetString(cat.Id));
        corpse.name = CatText.F("{0} · 猫尸", cat.Name);
        if (item.parentInventory is GameGridInventory inventory &&
            TryReplaceInOriginalSpace(item, corpse, inventory))
        {
            cat.CorpsePlaced = true;
            catItems.Remove(cat.Id);
            item.Destroy();
            return true;
        }

        // Preserve the physical item until there is space to place its replacement.
        EmporiumEntry? entry = EmporiumEntry.Instance;
        if (entry == null)
        {
            corpse.Destroy();
            return recorded;
        }
        if (!TryPlace(corpse, null, new[] { entry.invElement, entry.backInvinvElement, entry.frontInvinvElement }))
        {
            return recorded;
        }
        cat.CorpsePlaced = true;
        catItems.Remove(cat.Id);
        item.Destroy();
        return true;
    }

    private static bool TryReplaceInOriginalSpace(GameItem cat, GameItem corpse, GameGridInventory inventory)
    {
        GridShape occupied = cat.modifiedShape.Clone();
        if (occupied.width != 2 || occupied.height < 1 || !inventory.Expel(cat))
        {
            return false;
        }

        // The corpse occupies a strict subset of the space we just vacated.
        corpse.modifiedShape = new GridShapeBuilder().SetDataFill(2, 1).SetTransform(occupied).Build();
        if (inventory.UncheckedAccept(corpse))
        {
            return true;
        }
        cat.modifiedShape = occupied;
        inventory.UncheckedAccept(cat);
        return false;
    }

    private static IEnumerable<GameItem> GetTrashContents(EmporiumEntry entry)
    {
        if (entry.trashcanInvElement is GameGridInventory trashcan)
        {
            foreach (GameItem item in trashcan.childItems)
            {
                yield return item;
            }
        }
        if (entry.trashInvElement?.childItem is GameItem dropSlotItem)
        {
            yield return dropSlotItem;
        }
    }

    private void CaptureCleanupCorpses()
    {
        cleanupCorpses.Clear();
        if (!HasCattery() || EmporiumEntry.Instance is not EmporiumEntry entry ||
            StoreService.GetJanitorialService()?.IsActive() != true ||
            PlayerStore.Instance.exchangeManager?.isExchangeUnlocked != true ||
            StoreReputation.IsPerkUnlocked("BM_DISTRUSTED"))
        {
            return;
        }

        foreach (GameItem item in GetTrashContents(entry).Where(item => item.identifier == CatCorpseId))
        {
            string id = ReadCatId(item);
            if (cattery!.Cats.Any(cat => cat.Id == id && cat.CorpsePlaced && !cat.CorpseCleaned))
            {
                cleanupCorpses.Add(id);
            }
        }
    }

    private void SettleCleanupCorpses()
    {
        if (!HasCattery() || EmporiumEntry.Instance is not EmporiumEntry entry)
        {
            cleanupCorpses.Clear();
            return;
        }

        var remainingCorpses = new HashSet<string>(GetTrashContents(entry)
            .Where(item => item.identifier == CatCorpseId).Select(ReadCatId));
        bool changed = false;
        foreach (Cat cat in cattery!.Cats)
        {
            if (cleanupCorpses.Contains(cat.Id) && !remainingCorpses.Contains(cat.Id))
            {
                cat.CorpseCleaned = true;
                changed = true;
            }
        }
        cleanupCorpses.Clear();
        changed |= RestoreCleanupFood();
        if (changed)
        {
            SaveCattery();
        }
    }

    private bool RestoreCleanupFood()
    {
        if (cattery == null || EmporiumEntry.Instance?.trashcanInvElement is not GameGridInventory trashcan)
        {
            return false;
        }

        int delivered = 0;
        foreach (Cat cat in cattery.Cats.Where(cat => cat.CorpseCleaned && !cat.CleanupFoodDelivered))
        {
            GameItem food = ModHelper.SetItemOwnerShip(ModHelper.SpawnItem(CatFoodId), true);
            if (GraphUtils.TryAcceptAll(trashcan, food, 1) != 0)
            {
                if (food.parentInventory == null)
                {
                    food.Destroy();
                }
                break;
            }
            cat.CleanupFoodDelivered = true;
            delivered++;
        }
        if (delivered == 0)
        {
            return false;
        }
        PlayerStore.Instance.AddNightLog(CatText.F("黑市清洁服务在垃圾桶里留下了 {0} 份猫粮。", delivered));
        return true;
    }
}
