using System;
using System.Collections.Generic;
using System.Linq;

namespace DeepSpaceCattery;

public sealed partial class GameMod
{
    private const int CatFoodSize = 2;
    private const int MixedCatFoodMealKcal = Cattery.CatFoodKcal / 4;
    private const int LooseFoodThreshold = 55;
    private const int DispenserFeedingThreshold = 75;
    private const double RegularConceptionChance = .50;
    private const double NurseryConceptionChance = .70;
    private const int RegularConceptionNightLimit = 4;
    private const int NurseryConceptionNightLimit = 3;

    private string CatFoodId => modId + ".cat_food";
    private string CatFoodTag => modId + ".cat_food_item";

    private GameItem CreateCatFood()
    {
        GameItem food = ItemDirectory.CreateEmptyItem(CatFoodId);
        food.name = CatText.T("猫粮");
        ApplyCatFoodPresentation(food);
        food.SetShape(new GridShapeBuilder().SetDataFill(CatFoodSize, CatFoodSize).Build());
        food.EnableTag(CatFoodTag);
        food.mayStackItemFunc = (_, _) => false;
        food.maxSlotAmountItemFunc = (_, _, _) => 1;
        RefreshCatFoodCalories(food);
        food.onChangeAmountItemFunc += RefreshCatFoodCalories;
        food.onLoaded += item =>
        {
            ApplyCatFoodPresentation(item);
            item.RemoveGameItemType("FOOD");
            item.EnableTag(CatFoodTag);
            RefreshCatFoodCalories(item);
        };
        food.maySelectSlotItemFunc = (item, _, _) => GeneralHelper.IsItemOwned(item);
        food.canSelectSlotItemFunc = food.maySelectSlotItemFunc;
        return food;
    }

    private void ApplyCatFoodPresentation(GameItem food)
    {
        food.shortDescription = CatText.F("每份含 {0} 千卡，可直接喂猫，也可放入牲畜饲料分配器。", Cattery.CatFoodKcal) +
            CatText.T("吃了这种猫粮，幼猫长得更快，成年猫老得更慢，夜里出门也更容易有所收获。");
        food.SetValue(CatteryPrices.Food);
        food.useSpriteFromMod = true;
        food.SetSprite(modId, "cattery_cat_food");
    }

    private static void RefreshCatFoodCalories(GameItem food)
    {
        string caloriesTag = FoodItemHelper.CALORIE_VALUE_TAG;
        // Vanilla blends the entire item using its tag. Old multi-portion items keep every portion's calories.
        int calories = (int)Math.Clamp((long)food.unitCount * Cattery.CatFoodKcal, 0L, int.MaxValue - 40_000L);
        if (!food.IsTag(caloriesTag) || food.GetTagReadonly(caloriesTag).GetInt() != calories)
        {
            FoodItemHelper.SetCalories(food, calories);
        }
    }

    private bool CanStoreInHouse(GameItem house, GameItem item) => GeneralHelper.IsItemOwned(house, item) &&
        (IsCatItem(item) || item.identifier == CatFurId || IsHouseFood(item));

    private bool IsHouseFood(GameItem item) => IsCatFood(item) ||
        (item.IsGameItemType("FOOD") && !item.IsTag(HusbandryHelper.ANIMAL_ITEM_TAG) &&
            !GutterflowPelletHelper.IsPellet(item) && !IndustrialFishFeedHelper.IsFeed(item));

    private bool IsCatFood(GameItem item) => item.identifier == CatFoodId && item.IsTag(CatFoodTag);

    private static bool InstallStarterFeeder(GameItem house)
    {
        if (AnimalCage.GetDispenserSlot(house) is not GameSlotInventory slot)
            return false;
        if (slot.childItem != null)
            return slot.childItem.identifier == LivestockFeederId;

        var feeder = ModHelper.SetItemOwnerShip(DirectoryMaster.Item(LivestockFeederId, false), true);
        if (slot.TryFindOneValidInventorySlot(feeder)?.TryAcceptOnce(1) == 1)
            return true;
        feeder.Destroy();
        return false;
    }

    private static MachineryPanel CreateFeederPanel(GameItem house, out Action refresh)
    {
        var slot = new GameSlotInventory();
        slot.mayInventoryAddItemFunc = (item, _) => GeneralHelper.IsItemOwned(house, item) &&
            item.IsTag(MachineFeedDispenser.FEED_DISPENSER_TAG);
        slot.SetBackgroundFadeSprite("Items/items_husbandry", "animal_feeder", .5f, .5f);

        var blend = new MachineryButton(CatText.T("加工饲料"), () =>
        {
            GameItem? feeder = slot.childItem;
            if (feeder != null && MachineFeedDispenser.GetStorageGrid(feeder) is GameGridInventory food)
            {
                MachineFeedDispenser.Blend(feeder, food);
            }
        }).SetDescription(CatText.T("把猫粮和其他适用原料加工成饲料。"));
        void RefreshButton() => blend.SetHidden(slot.childItem == null);
        slot.onSlotAddItemEndFunc = (_, _, _) => RefreshButton();
        slot.onSlotRemoveItemEndFunc = (_, _, _) => RefreshButton();
        RefreshButton();
        refresh = RefreshButton;

        var panel = new GridPixelElement(1, 2, true).SetSpacing(0, 4);
        panel.Attach(MachineHelper.WrapInIntake(slot), 0, 0, 5, 5);
        panel.Attach(blend, 0, 1, 5, 5);
        return new MachineryPanel(CatText.T("喂食器"), panel, "feeder");
    }

    private bool RestoreCatFood()
    {
        if (cattery == null || cattery.Food <= 0 || EmporiumEntry.Instance is not EmporiumEntry entry)
        {
            return false;
        }
        GameItem? house = FindHouse();
        int placed = PlaceCatSupplyPortions(CatFoodId, cattery.Food,
            house == null ? null : MainCompartment(house), entry.invElement, entry.backInvinvElement);
        cattery.Food -= placed;
        return placed > 0;
    }

    private bool RestoreLooseCatSupplyItems()
    {
        if (EmporiumEntry.Instance is not EmporiumEntry entry)
        {
            return false;
        }

        bool changed = false;
        GameItem[] stacks = entry.GetAllOwnedItems(includeAfterhour: false).Where(item =>
            item.unitCount > 1 && (item.identifier == CatFoodId || item.identifier == CatFurId)).Distinct().ToArray();
        foreach (GameItem stack in stacks)
        {
            GameInventory? current = stack.parentInventory;
            GameItem? container = current?.GetParentItem();
            // The original auto blender can immediately consume the source stack when another portion is inserted.
            // This existing stack already advertises its full multi-portion calorie count.
            if (container != null && container.IsTag(MachineFeedDispenser.FEED_DISPENSER_TAG) &&
                MachineFeedDispenser.IsAutoBlendOn(container))
            {
                continue;
            }

            changed |= SplitCatSupplyPortions(stack, current as GameGridInventory,
                entry.backInvinvElement, entry.invElement) > 0;
        }
        return changed;
    }

    private int SplitCatSupplyPortions(GameItem stack, params GameGridInventory?[] destinations)
    {
        // Keep the source item and its last portion at its original location.
        int placed = PlaceCatSupplyPortions(stack.identifier, stack.unitCount - 1, destinations);
        if (placed > 0)
        {
            stack.SetAmount(stack.unitCount - placed);
            if (stack.identifier == CatFoodId)
            {
                RefreshCatFoodCalories(stack);
            }
        }
        return placed;
    }

    private static int PlaceCatSupplyPortions(string id, int requested, params GameGridInventory?[] destinations)
    {
        int placed = 0;
        while (placed < requested)
        {
            GameItem item = ModHelper.SetItemOwnerShip(ModHelper.SpawnItem(id), true);
            if (!destinations.Any(inventory => inventory != null &&
                GraphUtils.TryAcceptAll(inventory, item, amount: 1) == 0))
            {
                item.Destroy();
                break;
            }
            placed++;
        }
        return placed;
    }

    private sealed class HouseCompartments
    {
        public string Id { get; }
        public GameGridInventory Nest { get; }
        public GameGridInventory? Nursery { get; }
        public GameItem? Feeder { get; }
        public int TargetKcal { get; }

        public HouseCompartments(string id, GameGridInventory nest, GameGridInventory? nursery, GameItem? feeder)
        {
            Id = id;
            Nest = nest;
            Nursery = nursery;
            Feeder = feeder;
            TargetKcal = feeder == null ? Cattery.MaximumSatietyKcal
                : MachineFeedDispenser.GetTargetPercent(feeder) * Cattery.KcalPerSatietyPoint;
        }
    }

    private HouseCompartments[] GetHouseCompartments(EmporiumEntry entry) => FindHouses(entry)
        .Select(house => new HouseCompartments(GetHouseIdentity(house), MainCompartment(house)!,
            NurseryCompartment(house), AnimalCage.GetDispenserSlot(house)?.childItem)).ToArray();

    private Dictionary<Cat, GameGridInventory> ProcessHouseNight(int day, ISet<Cat> outsideForagers, CatNightReport nightReport)
    {
        var mealVisitors = new Dictionary<Cat, GameGridInventory>();
        if (EmporiumEntry.Instance is not EmporiumEntry entry)
        {
            return mealVisitors;
        }

        HouseCompartments[] houses = GetHouseCompartments(entry);
        var groups = houses.Select(house => (House: house, Nest: FindCompartmentResidents(house.Nest),
            Nursery: house.Nursery == null ? Array.Empty<Cat>() : FindCompartmentResidents(house.Nursery))).ToArray();
        foreach (var (house, nest, nursery) in groups)
        {
            foreach (Cat cat in nursery)
            {
                FeedFromHouse(cat, house, day, house.Nursery);
            }
            foreach (Cat cat in nest)
            {
                FeedFromHouse(cat, house, day);
            }
        }

        var roamingResidents = new List<(Cat Cat, HouseCompartments House)>();
        if (houses.Length > 0)
        {
            foreach (Cat cat in cattery!.Cats)
            {
                if (!cat.IsInCare || cat.GetStage(day) is not (AgeStage.Adult or AgeStage.Senior) ||
                    !catItems.TryGetValue(cat.Id, out GameItem? item) || item.parentInventory is not GameInventory source ||
                    (source.GetParentItem() is GameItem container && IsHouse(container)) || !CanRoamFrom(item, source, entry))
                {
                    continue;
                }

                string? homeId = GetRoamingHouseId(cat, houses[0].Id);
                HouseCompartments? home = houses.FirstOrDefault(house => house.Id == homeId);
                if (home != null)
                {
                    roamingResidents.Add((cat, home));
                    if (FeedFromHouse(cat, home, day))
                    {
                        mealVisitors.Add(cat, home.Nest);
                    }
                }
                else
                {
                    foreach (HouseCompartments host in houses)
                    {
                        if (FeedFromHouse(cat, host, day))
                        {
                            mealVisitors.Add(cat, host.Nest);
                            break;
                        }
                    }
                }
            }
        }

        SettleEmergencyHunger(day, entry, outsideForagers, nightReport);
        foreach (var (house, nest, nursery) in groups)
        {
            BreedCompartment(nest.Concat(roamingResidents.Where(resident => resident.House == house)
                .Select(resident => resident.Cat)), day, inNursery: false);
            BreedCompartment(nursery, day, inNursery: true);
        }
        return mealVisitors;
    }

    private Cat[] FindCompartmentResidents(GameGridInventory inventory)
    {
        var residents = new HashSet<string>(inventory.childItems.Where(IsCatItem).Select(ReadCatId));
        return cattery!.Cats.Where(cat => cat.Alive && cat.IsInCare && residents.Contains(cat.Id)).ToArray();
    }

    private bool FeedFromHouse(Cat cat, HouseCompartments house, int night,
        GameGridInventory? alternate = null)
    {
        GameItem? feeder = house.Feeder;
        int targetKcal = house.TargetKcal;
        bool fed = false;
        if (feeder != null && cat.StoredCalories < DispenserFeedingThreshold * Cattery.KcalPerSatietyPoint &&
            cat.StoredCalories < targetKcal)
        {
            int catFoodBefore = CatFoodInFeeder.GetTrackedCalories(feeder);
            int drawn = MachineFeedDispenser.DrawKcal(feeder, targetKcal - cat.StoredCalories);
            if (drawn > 0)
            {
                fed = true;
                AddSatiety(cat, drawn, targetKcal);
                if (catFoodBefore - CatFoodInFeeder.GetTrackedCalories(feeder) >= MixedCatFoodMealKcal)
                {
                    cat.RecordCatFoodMeal(night);
                }
            }
        }
        return FeedLooseMeal(cat, house.Nest, targetKcal, night) ||
            (alternate != null && FeedLooseMeal(cat, alternate, targetKcal, night)) || fed;
    }

    private bool FeedLooseMeal(Cat cat, GameGridInventory inventory, int targetKcal, int night) =>
        cat.StoredCalories < Math.Min(targetKcal, LooseFoodThreshold * Cattery.KcalPerSatietyPoint) &&
        (FeedPreferredFood(cat, inventory, night) || FeedLooseFood(cat, inventory));

    private void BreedCompartment(IEnumerable<Cat> cats, int day, bool inNursery)
    {
        Cattery state = cattery!;
        Cat[] eligible = cats.Where(cat => state.CanBreed(cat, day)).ToArray();
        Cat[] fathers = eligible.Where(cat => cat.IsMale).ToArray();
        if (fathers.Length == 0)
        {
            return;
        }
        double conceptionChance = inNursery ? NurseryConceptionChance : RegularConceptionChance;
        int nightLimit = inNursery ? NurseryConceptionNightLimit : RegularConceptionNightLimit;
        foreach (Cat mother in eligible.Where(cat => !cat.IsMale))
        {
            if (mother.FailedConceptionNights < nightLimit - 1 && random.NextDouble() >= conceptionChance)
            {
                mother.FailedConceptionNights++;
                continue;
            }
            Cat father = fathers[random.Next(fathers.Length)];
            state.TryStartPregnancy(mother, father, day, random.Next());
        }
    }

    private bool FeedPreferredFood(Cat cat, GameGridInventory inventory, int night)
    {
        GameItem? food = inventory.childItems.FirstOrDefault(item => IsCatFood(item) &&
            item.unitCount > 0 && GeneralHelper.IsItemOwned(item));
        return food != null && FeedCatFood(cat, food, night);
    }

    private bool FeedCatFood(Cat cat, GameItem food, int firstNight)
    {
        if (!IsCatFood(food) || !GeneralHelper.IsItemOwned(food) ||
            cat.StoredCalories >= Cattery.MaximumSatietyKcal || !cat.Alive || !cat.IsInCare)
        {
            return false;
        }
        bool hadSeveralPortions = food.unitCount > 1;
        if (!ConsumeOne(food))
        {
            return false;
        }
        if (hadSeveralPortions)
        {
            RefreshCatFoodCalories(food);
        }
        AddSatiety(cat, Cattery.CatFoodKcal);
        cat.RecordCatFoodMeal(firstNight);
        return true;
    }

    private bool FeedLooseFood(Cat cat, GameGridInventory inventory)
    {
        GameItem? food = inventory.childItems.FirstOrDefault(item => !IsCatFood(item) && IsHouseFood(item) &&
            item.unitCount > 0 && GeneralHelper.IsItemOwned(item) && !item.IsTag(FoodItemHelper.CANNED_FOOD_TAG) &&
            item.GetTagReadonly(FoodItemHelper.CALORIE_VALUE_TAG).GetInt() > 0);
        if (food == null)
        {
            return false;
        }
        int calories = food.GetTagReadonly(FoodItemHelper.CALORIE_VALUE_TAG).GetInt();
        if (!ConsumeOne(food))
        {
            return false;
        }
        AddSatiety(cat, calories);
        return true;
    }

    private static void AddSatiety(Cat cat, int calories, int targetKcal = Cattery.MaximumSatietyKcal)
    {
        int stored = cat.StoredCalories;
        cat.SetStoredCalories(stored + Math.Clamp(calories, 0, Math.Max(0, targetKcal - stored)));
    }
}
