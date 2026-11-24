namespace DeepSpaceCattery;

public sealed partial class GameMod
{
    private bool DropPendingCatFur()
    {
        if (cattery is not Cattery state || EmporiumEntry.Instance is not EmporiumEntry entry)
        {
            return false;
        }

        long released = 0;
        foreach (Cat cat in state.Cats)
        {
            if (cat.ShedFurPending <= 0)
            {
                continue;
            }

            GameGridInventory? origin = null;
            if (catItems.TryGetValue(cat.Id, out GameItem? item) && GeneralHelper.IsItemEffectivelyOwned(item))
            {
                origin = item.parentInventory as GameGridInventory;
            }
            long placed = PlaceShedCatFur(cat.ShedFurPending, origin, entry);
            cat.ShedFurPending -= placed;
            released += placed;
        }
        return released > 0;
    }

    private long PlaceShedCatFur(long quantity, GameGridInventory? origin, EmporiumEntry entry)
    {
        if (origin == entry.trashcanInvElement || origin == entry.frontInvinvElement ||
            origin == entry.backInvinvElementCounter || origin == entry.soldElement)
        {
            origin = null;
        }

        long placed = 0;
        while (placed < quantity)
        {
            var fur = ModHelper.SetItemOwnerShip(ModHelper.SpawnItem(CatFurId), true);
            if (TryPlaceInVisibleGrid(origin, fur) ||
                (origin != entry.backInvinvElement && TryPlaceInVisibleGrid(entry.backInvinvElement, fur)) ||
                (origin != entry.invElement && TryPlaceInVisibleGrid(entry.invElement, fur)))
            {
                placed++;
                continue;
            }
            fur.Destroy();
            break;
        }
        return placed;
    }
}
