using System.Collections.Generic;

namespace DeepSpaceCattery;

public sealed partial class GameMod
{
    private const string GeneScannerId = "gene_scanner";
    private const string BlueLabelerId = "labeler_blue";
    private const string BatteryId = "energy_credit";
    private const string PetShopMarkupFeatureId = "premiumBuy";

    private IEnumerable<GameItem> CreatePetShopGoods(bool forDayVisit)
    {
        yield return CreateGood(HouseId);

        foreach (GameItem good in Stock(CatFoodId, forDayVisit ? 2 : 6))
        {
            yield return good;
        }

        var equipment = new[]
        {
            LivestockFeederId, "small_morsel", "morsel", "small_raw_meat",
            GeneScannerId, NeuteringToolId, BlueLabelerId, "mouse_trap",
            FurProcessorId, BatteryId
        };
        foreach (string id in equipment)
        {
            yield return CreateGood(id);
        }

        static GameItem CreateGood(string id) => ModHelper.SetItemOwnerShip(ModHelper.SpawnItem(id), false);

        static IEnumerable<GameItem> Stock(string id, int count)
        {
            for (int index = 0; index < count; index++)
            {
                yield return CreateGood(id);
            }
        }
    }
}
