using System.Collections.Generic;
using System.Linq;

namespace DeepSpaceCattery;

public sealed partial class GameMod
{
    private string PetShopShelfId => modId + ".pet_shop_shelf";

    // Old stock fixtures may still be decoded from a save. New visits use the native counter directly.
    private GameItem CreatePetShopShelf()
    {
        var (window, storage) = DirectoryUtils.CreateInventoryWindow(10, 8, true);
        GameItem shelf = ItemDirectory.CreateEmptyItem(PetShopShelfId);
        shelf.name = CatText.T("宠物店货柜");
        shelf.shortDescription = CatText.T("旧式货柜，原本用来陈列宠物店的商品。");
        shelf.SetSpriteAndShape("Items/items_ship", "storage_bay");
        shelf.SetContentWindow(window);
        shelf.EnableTag(ContainerHelper.CONTAINER_TAG);
        shelf.EnableTag(ProtectedFromSale);
        shelf.EnableTag(GeneralHelper.IMPORTANT_TAG);
        shelf.mayInventoryRemoveItemFunc = (_, _) => false;
        shelf.maxSlotRemoveItemFunc = (_, _, _) => 0;
        storage.maxSlotAddItemFunc = (_, _, _) => 0;
        return shelf;
    }

    private static void PlacePetShopStock(GameGridInventory front, IEnumerable<GameItem> stock,
        bool prioritizeStockOrder = false)
    {
        var displayOrder = prioritizeStockOrder ? stock :
            stock.OrderByDescending(item => item.modifiedShape.width * item.modifiedShape.height);
        foreach (GameItem item in displayOrder)
        {
            if (item.parentInventory == null && item.unitCount == 1 && !GeneralHelper.IsItemOwned(item))
            {
                TryPlaceInDirectGrid(front, item);
            }
        }
    }

    private static bool TryPlaceInDirectGrid(GameGridInventory? inventory, GameItem item)
    {
        if (inventory == null || inventory.IsInsertLocked())
        {
            return false;
        }
        GridShape shape = item.modifiedShape;
        var position = new GridShapeBuilder(shape);
        // The automatic slot search can choose a container's contents before this grid itself.
        for (int row = 0; row < inventory.inventoryShape.height; row++)
        {
            for (int column = 0; column < inventory.inventoryShape.width; column++)
            {
                position.SetTransform(column, row, shape.flipped, shape.orientation);
                SlotMarker? slot = inventory.TryInventorySlot(item, 1, position, null);
                if (slot != null && slot.inventory == inventory && slot.targetItem == null && slot.numTransfer >= 1)
                {
                    return slot.TryAcceptOnce(1) == 1 && item.parentInventory == inventory;
                }
            }
        }
        return false;
    }
}
