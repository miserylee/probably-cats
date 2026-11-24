using HarmonyLib;

namespace DeepSpaceCattery;

public sealed partial class GameMod
{
    private const int FabricFurCost = 10;
    private const int FurProcessorPowerPerBatch = 4;
    private const int FurProcessorInputIndex = 1;
    private const int FurProcessorOutputIndex = 2;
    private const string FabricId = "kotton_fabric";

    private string CatFurId => modId + ".cat_fur";
    private string FurProcessorId => modId + ".fur_processor";

    private void RegisterFurItems(ModItemDirectory directory)
    {
        directory.Add(CatFurId, CreateCatFurItem);
        directory.Add(FurProcessorId, CreateFurProcessor);
    }

    private GameItem CreateCatFurItem()
    {
        GameItem item = ItemDirectory.CreateEmptyItem(CatFurId);
        item.name = CatText.T("猫毛");
        item.shortDescription = CatText.F("猫身上掉下的一团浮毛。集齐 {0} 份，可用猫毛加工器织成 1 份韧棉织物。", FabricFurCost);
        item.useSpriteFromMod = true;
        item.SetSprite(modId, "cattery_cat_fur");
        item.SetValue(CatteryPrices.Fur);
        item.SetShape(new GridShapeBuilder().SetDataFill(1, 1).Build());
        item.SetGameItemType("MATERIAL");
        item.mayStackItemFunc = (_, _) => false;
        item.maxSlotAmountItemFunc = (_, _, _) => 1;
        return item;
    }

    private GameItem CreateFurProcessor()
    {
        GameItem module = ItemDirectory.CreateEmptyItem(FurProcessorId);
        module.name = CatText.T("猫毛加工器");
        module.shortDescription = CatText.F("用电池供电，每晚自动收集店里的猫毛。每织成 1 份韧棉织物，消耗 {0} 份猫毛和 {1} 千焦电量。", FabricFurCost, FurProcessorPowerPerBatch);
        module.useSpriteFromMod = true;
        module.SetSprite(modId, "cattery_fur_processor");
        module.SetValue(CatteryPrices.FurProcessor);
        module.onLoaded += item => item.SetValue(CatteryPrices.FurProcessor);
        module.SetShape(new GridShapeBuilder().SetDataFill(3, 3).Build());
        module.SetGameItemType("MACHINE");
        module.EnableTag("CONTAINER_TAG");
        module.ModifyTag(MachineryHelper.MACHINERY_DRAWN_POWER, tag => tag.Enable().SetInt(FurProcessorPowerPerBatch));
        module.ModifyTag(MachineryHelper.MACHINERY_DRAWN_POWER_BASE, tag => tag.Enable().SetInt(FurProcessorPowerPerBatch));

        var input = new GameGridInventory(8, 5);
        input.mayInventoryAddItemFunc = (item, _) => item.identifier == CatFurId &&
            GeneralHelper.IsItemOwned(module, item);
        // A native piece of Kotton Fabric spans three horizontal cells.
        var output = new GameGridInventory(9, 4) { overrideLockInsert = true };
        output.mayInventoryAddItemFunc = (item, _) => ReferenceEquals(item, fabricBeingPlaced) &&
            GeneralHelper.IsItemOwned(module, item);
        var power = new GameSlotInventory();
        MachineHelper.SetupBatterySlot(power, module);
        power.mayInventoryAddItemFunc = (item, _) => IsFurProcessorBattery(item) &&
            GeneralHelper.IsItemOwned(module, item);

        module.SetContentWindow(MachineHelper.CreateMachineryWindow(true,
            MachineHelper.PowerPanel(power),
            MachineHelper.Panel(CatText.T("进料"), input, "input"),
            MachineHelper.Panel(CatText.T("出料"), output, "output")));
        return module;
    }
}

[HarmonyPatch(typeof(GameItemElement), "ValidateSelectLocked")]
internal static class CatFurGridHitTest
{
    internal static string? ItemId { get; set; }

    private static void Postfix(GameItemElement __instance)
    {
        if (ItemId != null && __instance.identifier == ItemId)
        {
            // Let the native drag handler find the item anywhere in its occupied cell.
            // The clear area around the fine strands remains visually transparent.
            __instance.background.image.alphaHitTestMinimumThreshold = 0;
        }
    }
}
