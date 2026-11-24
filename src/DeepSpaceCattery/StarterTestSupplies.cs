namespace DeepSpaceCattery;

public sealed partial class GameMod
{
    private string StarterTestCrateId => modId + ".starter_test_crate";

    private GameItem CreateStarterTestCrate()
    {
        // Retain the item factory so 0.2.1 saves can load their contents.
        GameItem crate = ItemDirectory.CreateEmptyItem(StarterTestCrateId);
        crate.name = CatText.T("旧版测试物资箱");
        crate.shortDescription = CatText.T("早期测试用的物资箱。箱里剩下的东西仍可取出。");
        crate.SetSpriteAndShapeFromMod(modId, "portable_cattery");
        crate.SetShape(new GridShapeBuilder().SetDataFill(1, 1).Build());
        crate.SetValue(CatteryPrices.DebugItem);
        ProtectItem(crate);
        crate.EnableTag("CONTAINER_TAG");
        crate.SetContentWindow(MachineHelper.CreateMachineryWindow(true,
            MachineHelper.Panel(CatText.T("测试物资"), new GameGridInventory(10, 8), "supplies")));
        return crate;
    }
}
