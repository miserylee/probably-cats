using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace DeepSpaceCattery;

public sealed partial class GameMod
{
    private const string PetShopDeliveriesSaveKey = "pet-shop-deliveries-v1";

    private sealed class PetShopDelivery
    {
        public string ItemId { get; set; } = string.Empty;
        public string PackedItem { get; set; } = string.Empty;
        public Cat? Adopted { get; set; }
        public string? OwnedCatId { get; set; }
    }

    private bool HasPendingPetShopAdoption() =>
        ReadPetShopDeliveries().Any(delivery => delivery.Adopted != null);

    private bool HasPendingPetShopHouse() =>
        ReadPetShopDeliveries().Any(delivery => delivery.ItemId == HouseId);

    private HashSet<string> PendingPetShopCatIds() => ReadPetShopDeliveries()
        .Select(delivery => delivery.OwnedCatId).OfType<string>().ToHashSet();

    private void DeliverPetShopPurchase(GameItem item, Cat? adopted)
    {
        // The native purchase callback runs before ownership and payment are committed.
        if (!GeneralHelper.IsItemOwned(item) || item.unitCount <= 0)
        {
            return;
        }

        string name = item.GetDisplayName(preferCustomName: true);
        if (EmporiumEntry.Instance is EmporiumEntry entry && TryDeliverPetShopItem(entry, item, out string destination))
        {
            CompletePetShopDelivery(item, adopted, name, destination);
            return;
        }

        var pending = ReadPetShopDeliveries();
        pending.Add(new PetShopDelivery
        {
            ItemId = item.identifier,
            PackedItem = JsonUtility.ToJson(new SaveState { saveItems = SaveManager.EncodeNodes(item) }),
            Adopted = adopted,
            OwnedCatId = adopted == null && IsCatItem(item) ? ReadCatId(item) : null
        });
        WritePetShopDeliveries(pending);
        item.Destroy();
        activeStore?.AddNightLog(CatText.F("当铺收货区放不下「{0}」，宠物店会在腾出空位后送来。", name));
    }

    private void RestorePetShopDeliveries()
    {
        if (cattery == null || activeStore == null || ModHelper.IsOutsideStore() ||
            EmporiumEntry.Instance is not EmporiumEntry entry)
        {
            return;
        }

        List<PetShopDelivery> pending = ReadPetShopDeliveries();
        if (pending.Count == 0)
        {
            return;
        }

        var remaining = new List<PetShopDelivery>(pending.Count);
        foreach (PetShopDelivery delivery in pending)
        {
            GameItem? item = UnpackPetShopDelivery(delivery);
            if (item == null)
            {
                remaining.Add(delivery);
                continue;
            }

            string name = item.GetDisplayName(preferCustomName: true);
            if (TryDeliverPetShopItem(entry, item, out string destination))
            {
                CompletePetShopDelivery(item, delivery.Adopted, name, destination);
            }
            else
            {
                item.Destroy();
                remaining.Add(delivery);
            }
        }
        if (remaining.Count != pending.Count)
        {
            WritePetShopDeliveries(remaining);
        }
    }

    private GameItem? UnpackPetShopDelivery(PetShopDelivery delivery)
    {
        try
        {
            if (JsonUtility.FromJson<SaveState>(delivery.PackedItem)?.saveItems is not { Count: > 0 } nodes ||
                SaveManager.DecodeNodes(nodes) is not GameItem item)
            {
                return null;
            }
            item.onLoaded?.Invoke(item);
            foreach (GameItem child in item.FindAllChildItems())
            {
                child.onLoaded?.Invoke(child);
            }
            return item;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or NullReferenceException)
        {
            log.Error(error);
            return null;
        }
    }

    private static bool TryDeliverPetShopItem(EmporiumEntry entry, GameItem item, out string destination)
    {
        int units = item.unitCount;
        if (units <= 0)
        {
            destination = string.Empty;
            return false;
        }
        GameInventory? source = item.parentInventory;
        GridShape originalShape = item.modifiedShape.Clone();
        foreach (var (inventory, label) in new[]
        {
            (entry.backInvinvElement, CatText.T("后区柜台")),
            (entry.invElement, CatText.T("店内收纳")),
            (entry.frontInvinvElement, CatText.T("前柜台"))
        })
        {
            if (inventory == null)
            {
                continue;
            }
            if (item.parentInventory == inventory)
            {
                destination = label;
                return true;
            }
            if (inventory.IsInsertLocked() || item.MaxNumRemove() < units)
            {
                continue;
            }
            SlotMarker? slot = inventory.TryFindOneValidInventorySlot(item);
            if (slot?.inventory != inventory || slot.numTransfer < units)
            {
                continue;
            }
            if (slot.TryAcceptOnce(units) == units)
            {
                destination = label;
                return true;
            }
            if (item.parentInventory == null)
            {
                item.modifiedShape = originalShape;
                source?.UncheckedAccept(item);
            }
        }

        destination = string.Empty;
        return false;
    }

    private void CompletePetShopDelivery(GameItem item, Cat? adopted, string name, string destination)
    {
        if (adopted != null)
        {
            int day = CurrentDay();
            // A cat remains in the shop's care until it reaches the player's inventory.
            adopted.BirthDay += Math.Max(0, day - adopted.AcquiredDay);
            adopted.AcquiredDay = day;
            FinishPetShopAdoption(adopted, item);
            name = item.GetDisplayName(preferCustomName: true);
        }
        activeStore?.AddNightLog(CatText.F("宠物店送来了「{0}」，放在{1}。", name, destination));
    }

    private List<PetShopDelivery> ReadPetShopDeliveries()
    {
        string data = ModHelper.GetModData(modId, PetShopDeliveriesSaveKey, string.Empty);
        try
        {
            return string.IsNullOrEmpty(data) ? new List<PetShopDelivery>() :
                JsonConvert.DeserializeObject<List<PetShopDelivery>>(data) ?? new List<PetShopDelivery>();
        }
        catch (JsonException error)
        {
            log.Error(error);
            return new List<PetShopDelivery>();
        }
    }

    private void WritePetShopDeliveries(List<PetShopDelivery> pending) =>
        ModHelper.SetModData(modId, PetShopDeliveriesSaveKey, JsonConvert.SerializeObject(pending));
}
