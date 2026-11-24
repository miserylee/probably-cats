namespace DeepSpaceCattery;

public sealed partial class GameMod
{
    private const int DebugFoodColumns = 4;
    private const int DebugFoodRows = 3;
    private const int DebugFoodOutputCapacity = DebugFoodColumns * DebugFoodRows;

    private string CatFoodDebugGeneratorId => modId + ".cat_food_debug_generator";

    private GameItem CreateCatFoodDebugGenerator()
    {
        GameItem generator = ItemDirectory.CreateEmptyItem(CatFoodDebugGeneratorId);
        generator.name = CatText.T("猫粮生成器（调试）");
        generator.shortDescription = CatText.F("测试用的猫粮生成器。启动时会填满出料区，最多可装 {0} 份猫粮。", DebugFoodOutputCapacity);
        generator.SetValue(CatteryPrices.DebugItem);
        generator.SetSpriteAndShape("Items/items_tool", "bottle_printer");
        generator.SetGameItemType("MACHINE");
        generator.EnableTag("CONTAINER_TAG");
        ProtectItem(generator);

        var output = new GameGridInventory(DebugFoodColumns * CatFoodSize, DebugFoodRows * CatFoodSize)
        {
            overrideLockInsert = true
        };
        bool generating = false;
        output.mayInventoryAddItemFunc = (item, _) => generating && item.identifier == CatFoodId &&
            GeneralHelper.IsItemOwned(generator, item);

        var status = MachineryText.Status(CatText.T("状态"), CatText.T("等待生成"));
        var generate = new MachineryButton(CatText.T("生成猫粮"), () =>
        {
            if (!enabled || !GeneralHelper.IsItemOwned(generator) || generating)
            {
                return;
            }

            int placed;
            generating = true;
            output.overrideLockInsert = false;
            try
            {
                placed = PlaceCatSupplyPortions(CatFoodId, DebugFoodOutputCapacity, output);
            }
            finally
            {
                output.overrideLockInsert = true;
                generating = false;
            }
            status.SetText(placed > 0 ? CatText.F("已生成 {0} 份猫粮", placed) : CatText.T("请先取走出料格内的猫粮"));
        }).SetDescription(CatText.F("每份猫粮占 {0}×{1} 格。出料区最多放 {2} 份，取走后可以再次生成。", CatFoodSize, CatFoodSize, DebugFoodOutputCapacity));

        generator.SetContentWindow(MachineHelper.CreateMachineryWindow(true,
            MachineHelper.Panel(CatText.T("出料"), output, "output"),
            MachineHelper.Panel(CatText.T("调试"), MachineryStack.Vertical(generate, status), "debug")));
        return generator;
    }
}
