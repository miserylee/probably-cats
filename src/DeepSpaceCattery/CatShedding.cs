using System.Collections.Generic;
using System.Linq;

namespace DeepSpaceCattery;

public sealed partial class GameMod
{
    private GameItem? fabricBeingPlaced;

    private void ProcessCatFurProcessors()
    {
        if (EmporiumEntry.Instance is not EmporiumEntry entry)
        {
            return;
        }

        GameItem[] items = entry.GetAllOwnedItems(includeAfterhour: false).Distinct().ToArray();
        GameItem[] processors = items.Where(item => item.identifier == FurProcessorId).ToArray();
        if (processors.Length == 0)
        {
            return;
        }

        var processorInputs = new HashSet<GameInventory>(processors
            .Select(item => MachineHelper.GetBodyStorage(item, FurProcessorInputIndex)).OfType<GameInventory>());
        GameItem[] looseFur = items.Where(IsOwnedCatFur).ToArray();
        foreach (GameItem processor in processors)
        {
            if (!GeneralHelper.IsItemEffectivelyOwned(processor) ||
                processor.parentInventory is not GameInventory location ||
                !IsAccessibleStoreInventory(entry, location) ||
                MachineHelper.GetBodyStorage(processor, FurProcessorInputIndex) is not GameGridInventory input ||
                MachineHelper.GetBodyStorage(processor, FurProcessorOutputIndex) is not GameGridInventory output ||
                MachineHelper.GetBatterySlot(processor) is not GameSlotInventory power ||
                power.childItem is not GameItem battery || !IsFurProcessorBattery(battery))
            {
                continue;
            }
            while (MachineHelper.CanPower(processor, power))
            {
                CollectLooseCatFur(entry, input, processorInputs, looseFur);
                if (!TryProcessFurBatch(processor, power, battery, input, output))
                {
                    break;
                }
            }
        }
    }

    private void CollectLooseCatFur(EmporiumEntry entry, GameGridInventory input,
        HashSet<GameInventory> processorInputs, IEnumerable<GameItem> looseFur)
    {
        foreach (GameItem item in looseFur)
        {
            if (!IsOwnedCatFur(item) || !item.MayRemove() || item.parentInventory is not GameInventory source ||
                processorInputs.Contains(source) || !IsAccessibleStoreInventory(entry, source))
            {
                continue;
            }

            int amount = item.MaxNumRemove();
            if (amount > 0 && GraphUtils.TryAcceptAll(input, item, amount) != 0)
            {
                return;
            }
        }
    }

    private static bool IsAccessibleStoreInventory(EmporiumEntry entry, GameInventory? inventory)
    {
        // Parent inventories may be inaccessible even if the child still bears a player-owned tag.
        while (inventory != null)
        {
            if (inventory == entry.trashcanInvElement || inventory == entry.trashInvElement ||
                inventory == entry.drainInvElement || inventory == entry.soldElement || inventory.IsRemoveLocked())
            {
                return false;
            }

            GameItem? parent = inventory.GetParentItem();
            if (parent == null)
            {
                return true;
            }
            if (!GeneralHelper.IsItemOwned(parent))
            {
                return false;
            }
            inventory = parent.parentInventory;
        }
        return false;
    }

    private bool TryProcessFurBatch(GameItem processor, GameSlotInventory power, GameItem battery,
        GameGridInventory input, GameGridInventory output)
    {
        List<GameItem> fur = input.childItems.Where(IsOwnedCatFur).ToList();
        if (fur.Sum(item => (long)item.unitCount) < FabricFurCost || fabricBeingPlaced != null)
        {
            return false;
        }

        GameItem fabric = ModHelper.SetItemOwnerShip(ModHelper.SpawnItem(FabricId), true);
        bool wasLocked = output.overrideLockInsert;
        int energy = battery.GetTagReadonly(PowerHelper.POWER_SOURCE_ITEM_ENERGY).GetInt();
        bool drewPower = false;
        bool placed = false;
        fabricBeingPlaced = fabric;
        output.overrideLockInsert = false;
        try
        {
            SlotMarker? slot = output.TryFindOneValidInventorySlot(fabric);
            if (slot == null || slot.inventory != output || !MachineHelper.TryDrawCyclePower(processor, power))
            {
                return false;
            }
            drewPower = true;
            placed = slot.TryAcceptOnce(1) == 1;
            if (!placed)
            {
                return false;
            }
            GraphUtils.TryDestroyAll(fur, amount: FabricFurCost);
            return true;
        }
        finally
        {
            output.overrideLockInsert = wasLocked;
            fabricBeingPlaced = null;
            if (!placed)
            {
                fabric.Destroy();
                if (drewPower && battery.GetTagReadonly(PowerHelper.POWER_SOURCE_ITEM_ENERGY).GetInt() != energy)
                {
                    PowerHelper.SetPowerSourceAt(battery, energy);
                }
            }
        }
    }

    private static bool IsFurProcessorBattery(GameItem item) =>
        item.IsTag(PowerHelper.POWER_SOURCE_ITEM_TAG) && !CurrentConverterHelper.IsCurrentConverter(item) &&
        GeneralHelper.IsItemOwned(item);

    private bool IsOwnedCatFur(GameItem item) => item.identifier == CatFurId &&
        item.unitCount > 0 && GeneralHelper.IsItemEffectivelyOwned(item);
}
