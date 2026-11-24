using System;
using System.Linq;
using UnityEngine;

namespace DeepSpaceCattery;

public sealed partial class GameMod
{
    private bool TryEatWarehouseFood(EmporiumEntry entry, Cat cat, int day, CatNightReport nightReport)
    {
        var meals = entry.GetAllOwnedItems(includeAfterhour: false)
            .Select(item => (Item: item, IsLiveRat: IsLiveWarehouseRat(item)))
            .Where(meal => meal.IsLiveRat || IsHouseFood(meal.Item) &&
                !meal.Item.IsTag(FoodItemHelper.CANNED_FOOD_TAG) &&
                meal.Item.GetTagReadonly(FoodItemHelper.CALORIE_VALUE_TAG).GetInt() > 0)
            .OrderByDescending(meal => meal.IsLiveRat);

        foreach (var (item, isLiveRat) in meals)
        {
            if (item.unitCount <= 0 || item.parentInventory is not GameGridInventory source ||
                !item.MayRemove() || GeneralHelper.IsItemLocked(item) || GeneralHelper.IsDisplayOnly(item) ||
                item.IsTag(GeneralHelper.JUST_SOLD_TAG) || !IsWarehouseStock(entry, source))
            {
                continue;
            }

            if (IsCatFood(item))
            {
                if (FeedCatFood(cat, item, day))
                {
                    nightReport.Add(cat, CatText.T("翻到仓库里的一份猫粮，把它吃了。"));
                    return true;
                }
                continue;
            }

            int calories = isLiveRat ? WarehouseRatCalories(item) :
                item.GetTagReadonly(FoodItemHelper.CALORIE_VALUE_TAG).GetInt();
            string foodName = isLiveRat ? CatText.T("一只老鼠") : CatText.F("「{0}」", item.GetDisplayName(preferCustomName: true));
            if (ConsumeOne(item))
            {
                AddSatiety(cat, calories);
                nightReport.Add(cat, isLiveRat ? CatText.F("在仓库抓住并吃了{0}。", foodName) : CatText.F("偷吃了仓库里的{0}。", foodName));
                return true;
            }
        }
        return false;
    }

    private static bool IsLiveWarehouseRat(GameItem item) => item.IsTag(HusbandryHelper.ANIMAL_ITEM_TAG) &&
        HusbandryHelper.GetSpecies(item) == AnimalSpecies.Rat && !HusbandryHelper.IsAnimalDead(item);

    private static int WarehouseRatCalories(GameItem rat)
    {
        int grams = Math.Max(0, Mathf.RoundToInt(rat.GetTagReadonly(HusbandryHelper.ANIMAL_WEIGHT_TAG).GetFloat()));
        if (rat.IsTag(HusbandryHelper.ANIMAL_DISEASED_TAG))
        {
            grams = Mathf.RoundToInt(grams * .75f);
        }

        // Match the original butcher yield without spawning meat items.
        int calories = grams / 400 * 1200;
        grams %= 400;
        calories += grams / 200 * 550;
        grams %= 200;
        calories += grams / 100 * 275;
        grams %= 100;
        return calories + (grams + 49) / 50 * 140;
    }

    private bool IsWarehouseStock(EmporiumEntry entry, GameInventory source)
    {
        if (!IsAccessibleStoreInventory(entry, source))
        {
            return false;
        }

        for (GameInventory? inventory = source; inventory != null;)
        {
            if (inventory.GetParentItem() is not GameItem parent)
            {
                return inventory == entry.invElement || inventory == entry.backInvinvElement;
            }
            if (IsHouse(parent) || !parent.IsTag(ContainerHelper.CONTAINER_TAG) || GeneralHelper.IsItemLocked(parent) ||
                LockHelper.IsCurrentlyLocked(parent) || parent.IsGameItemType("MACHINE") ||
                parent.IsTag(MachineryHelper.MACHINERY_TAG) || parent.IsTag(AnimalCage.CAGE_TAG) ||
                parent.IsTag(MachineFeedDispenser.FEED_DISPENSER_TAG) || parent.IsTag(RatTrap.MOUSE_TRAP_TAG))
            {
                return false;
            }
            inventory = parent.parentInventory;
        }
        return false;
    }
}
