using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using Random = System.Random;

namespace DeepSpaceCattery;

public sealed partial class GameMod : IMod
{
    private const string SaveKey = "cattery-v1";
    private const int CatLoversGiftFood = 12;
    private const int CatLoversReferralPrice = 750;

    private readonly Random random = new Random();
    private string modId = string.Empty;
    private ModManifest manifest = null!;
    private ModLog log = null!;
    private PlayerStore? activeStore;
    private int activeSlot = -1;
    private Cattery? cattery;
    private bool enabled;
    private bool catLoversServiceRegistered;

    private string ScenarioId => modId + ".pet_shop_start";
    private string PortableCatteryId => modId + ".portable_cattery";
    private string SpriteKey(string name) => modId + ":" + name;

    public void Init(ModManifest manifest)
    {
        modId = manifest.ID;
        this.manifest = manifest;
        log = new ModLog(manifest);
    }

    [HarmonyLib.HarmonyPatch(typeof(ModUIManager), nameof(ModUIManager.OpenPanel))]
    private static class LocalizeModListing
    {
        internal static ModManifest? ActiveManifest;

        private static void Prefix()
        {
            if (ActiveManifest == null) return;
            ActiveManifest.Name = CatText.T("深空猫舍");
            ActiveManifest.Description = CatText.T("在猫舍自然繁育，双击猫看性状和三代家谱。用金属锭扩建猫窝，用韧棉织物建造与扩建繁育间，用电池驱动猫毛加工器收毛制布。");
        }
    }

    public void OnEnable()
    {
        if (enabled)
        {
            return;
        }
        enabled = true;
        CatText.GetLocale = LocHelper.GetCurrentLocaleCode;
        LocalizeModListing.ActiveManifest = manifest;
        CatFoodInFeeder.FoodTag = CatFoodTag;
        CatFurGridHitTest.ItemId = CatFurId;
        EnableNativeCatInteractions();
        RegisterCatPedigreeClick();
        catNamingOwner = this;
        portraits = new CatPortraits(modId);
        petShopTradeOwner = this;
        ModHook.OnModAssetsLoaded += LoadInventoryArt;
        ModHook.OnModItemDirectoryInit += RegisterItems;
        ModHook.OnGameLoadedNormal += ReloadSave;
        ModHook.OnGameLoadedLate += RestoreLoadedCatItems;
        ModHook.OnNewGameStarted += RegisterCatLoversOnNewGame;
        ModHook.OnGoingSleepLate += GoToSleep;
        ModHook.OnWakingUpEarly += WakeUp;
        ModHook.OnReturningStoreLate += RestorePetShopDeliveries;
        ModHook.OnShutterOpenedEarly += OpenCatDay;
        ModHook.OnGenerateCustomerNormal += QueuePetShopVisitor;
        ModHook.OnGenerateCustomerLate += QueueWeeklyPetShopSupplier;
        ModHook.OnGenerateCustomerVeryLate += QueueShowcaseGuestAfterNormalClients;
        ModHook.OnCreateTooltipLate += AddCatPedigreeTooltip;
        ModHook.OnCreateTooltipLate += AddCatMarketTooltip;
        ModHook.OnCreateTooltipLate += AddHouseUpgradeTooltip;
        ModHook.OnPlaceInventorInventoryItemLate += StockScanner;
        ModHook.OnHandlingNightlyServicesEarly += CaptureCleanupCorpses;
        ModHook.OnHandlingNightlyServicesLate += SettleCleanupCorpses;
        ModHelper.AddStartingScenario(modId, ScenarioId,
            title: () => CatText.T("宠物店"),
            description: () => CatText.T("带着一座猫舍来到深空当铺，里面住着两只猫。照料它们，也可以培育下一代。"),
            startWith: new Func<string>[]
            {
                () => CatText.T("成年公猫和成年母猫各一只"),
                () => CatText.F("猫粮 {0} 份", Cattery.StarterFood),
                () => CatText.T("猫舍"),
                () => CatText.T("喂食机 1 台")
            },
            onStart: StartPetShop, iconKey: SpriteKey("pet_shop_scenario"));
        DelegateAfterhourLocation location = ModHelper.AddAfterhourLocation(
            modId, modId + ".pet_shop", () => CatText.T("宠物店"),
            onArrive: EnterPetShop, onLeave: LeavePetShop,
            imageKey: SpriteKey("cattery_shop"), backgroundKey: SpriteKey("cattery_shop_scene_merchant"),
            isDiscovered: LoadSaveIfNeeded);
        location.usesAfterhourBag = true;
        location.showsStoreCounters = true;
        location.backgroundSortingOrder = 0;
        location.leaveWarning = () => CatText.T("留在地面的自有物品会送回当铺。");
        location.frontCounterPosition = (width, height) =>
            AfterhourLocation.Get(AfterhourLocation.DR_JACKSON)?.GetFrontCounterPosition(width, height) -
            new Vector2(width / 120f, 0f);
        location.backCounterPosition = (width, height) =>
            AfterhourLocation.Get(AfterhourLocation.DR_JACKSON)?.GetBackCounterPosition(width, height) -
            new Vector2(width / 120f, height * 0.018f);
        log.Log("宠物店出身已注册。");
    }

    public void OnDisable()
    {
        enabled = false;
        CatText.GetLocale = null;
        LocalizeModListing.ActiveManifest = null;
        CatFoodInFeeder.FoodTag = null;
        CatFurGridHitTest.ItemId = null;
        UnregisterCatPedigreeClick();
        if (ReferenceEquals(catNamingOwner, this))
        {
            catNamingOwner = null;
        }
        DisableNativeCatInteractions();
        ModHook.OnModAssetsLoaded -= LoadInventoryArt;
        ModHook.OnModItemDirectoryInit -= RegisterItems;
        ModHook.OnGameLoadedNormal -= ReloadSave;
        ModHook.OnGameLoadedLate -= RestoreLoadedCatItems;
        ModHook.OnNewGameStarted -= RegisterCatLoversOnNewGame;
        ModHook.OnGoingSleepLate -= GoToSleep;
        ModHook.OnWakingUpEarly -= WakeUp;
        ModHook.OnReturningStoreLate -= RestorePetShopDeliveries;
        ModHook.OnShutterOpenedEarly -= OpenCatDay;
        ModHook.OnGenerateCustomerNormal -= QueuePetShopVisitor;
        ModHook.OnGenerateCustomerLate -= QueueWeeklyPetShopSupplier;
        ModHook.OnGenerateCustomerVeryLate -= QueueShowcaseGuestAfterNormalClients;
        ModHook.OnCreateTooltipLate -= AddCatPedigreeTooltip;
        ModHook.OnCreateTooltipLate -= AddCatMarketTooltip;
        ModHook.OnCreateTooltipLate -= AddHouseUpgradeTooltip;
        ModHook.OnPlaceInventorInventoryItemLate -= StockScanner;
        ModHook.OnHandlingNightlyServicesEarly -= CaptureCleanupCorpses;
        ModHook.OnHandlingNightlyServicesLate -= SettleCleanupCorpses;
        ModHelper.RemoveStartingScenarios(modId);
        ModHelper.RemoveAfterhourLocations(modId);
        ModHelper.RemoveNetworkUpgrades(modId);
        catLoversServiceRegistered = false;
        ResetRun();
        SpriteDict.Instance?.spriteDictionary?.Remove(SpriteKey(PetShopOwnerArt));
        UnityEngine.Object.Destroy(petShopOwnerSprite);
        petShopOwnerSprite = null;
        if (ReferenceEquals(petShopTradeOwner, this))
        {
            petShopTradeOwner = null;
        }
        portraits?.Dispose();
        portraits = null;
    }

    private void RegisterItems(ModItemDirectory directory)
    {
        directory.Add(PortableCatteryId, () => CreateHouse(legacy: true));
        directory.Add(PreviousHouseId, () => CreateHouse(legacy: false, previous: true));
        directory.Add(HouseId, () => CreateHouse(legacy: false));
        directory.Add(AdultCatId, () => CreateCatItem(kitten: false));
        directory.Add(KittenId, () => CreateCatItem(kitten: true));
        directory.Add(CatFoodId, CreateCatFood);
        directory.Add(CatFoodDebugGeneratorId, CreateCatFoodDebugGenerator);
        directory.Add(StarterTestCrateId, CreateStarterTestCrate);
        directory.Add(PetShopShelfId, CreatePetShopShelf);
        RegisterFurItems(directory);
        RegisterLifecycleItems(directory);
    }

    private void StartPetShop()
    {
        ReloadSave();
        if (cattery == null || !cattery.StartPetShop(CurrentDay(), random))
        {
            return;
        }

        RestoreCatItems();
        SaveCattery();
    }

    private void ReloadSave()
    {
        ResetRun();
        LoadSaveIfNeeded();
    }

    private void RegisterCatLoversOnNewGame(string _) => RegisterCatLoversService();

    private void RegisterCatLoversService()
    {
        // On 0.46D, AddNetworkUpgrade reads PlayerStore.Instance and creates a store
        // if none exists. The game is unable to construct a store from the main menu.
        if (!PlayerStore.HasInstance)
        {
            return;
        }

        string upgradeId = modId + ".cat_lovers";
        string iconKey = SpriteKey("cat_lovers_referral");
        if (!catLoversServiceRegistered)
        {
            ModHelper.AddNetworkUpgrade(modId, upgradeId, cost: CatLoversReferralPrice, subtitle: "Referral",
                title: () => CatText.T("猫咪爱好者"),
                description: () => CatText.F("收到一只成年猫、猫舍和 {0} 份猫粮。", CatLoversGiftFood),
                onUnlock: ClaimCatLoversGift, iconKey: iconKey, recommended: true);
            catLoversServiceRegistered = true;
        }

        // The native loader keeps existing runtime upgrade entries, including their prices.
        if (ModHelper.GetNetworkUpgrade(upgradeId) is NetworkUpgrade upgrade)
        {
            upgrade.cost = CatLoversReferralPrice;
            upgrade.subtitle = "Referral";
            upgrade.iconKey = iconKey;
            upgrade.recommended = true;
        }
    }

    private void ClaimCatLoversGift()
    {
        if (!LoadSaveIfNeeded())
        {
            return;
        }
        cattery!.AddFounder(CurrentDay(), random, random.Next(2) == 1);
        cattery.StarterClaimed = true;
        cattery.Food += CatLoversGiftFood;
        RestoreCatItems();
        SaveCattery();
    }

    private void ResetRun()
    {
        LeavePetShop();
        CloseDayPetShopStock();
        activeStore = null;
        activeSlot = -1;
        cattery = null;
        ResetCatPedigree();
        ResetCatSaleVisitor();
        catItems.Clear();
    }

    private bool LoadSaveIfNeeded()
    {
        if (!enabled || !PlayerStore.HasInstance || PlayerStore.Instance == null)
        {
            return false;
        }

        PlayerStore store = PlayerStore.Instance;
        if (ReferenceEquals(activeStore, store) && activeSlot == store.saveSlotId)
        {
            return cattery != null;
        }
        ResetRun();
        activeStore = store;
        activeSlot = store.saveSlotId;

        string json = ModHelper.GetModData(modId, SaveKey, string.Empty);
        try
        {
            cattery = string.IsNullOrEmpty(json) ? new Cattery() : JsonConvert.DeserializeObject<Cattery>(json);
            if (cattery == null || cattery.Cats == null || cattery.BreedingJobs == null)
            {
                throw new JsonSerializationException("猫舍记录缺少必要字段。");
            }
            foreach (Cat cat in cattery.Cats)
            {
                cat.GetPhenotype();
            }
            return true;
        }
        catch (Exception error) when (error is JsonException || error is ArgumentException || error is NullReferenceException)
        {
            cattery = null;
            log.Error(error);
            return false;
        }
    }

    private void SaveCattery()
    {
        ModHelper.SetModData(modId, SaveKey, JsonConvert.SerializeObject(cattery));
    }

    private static int CurrentDay() => StoreStation.GetDayCounter();

    private bool HasCattery() => LoadSaveIfNeeded() && cattery!.StarterClaimed;

    private void GoToSleep()
    {
        if (LoadSaveIfNeeded())
        {
            // The game increments its day and saves after the late sleeping hook.
            int morning = CurrentDay() + 1;
            Advance(morning);
            DeliverPendingCatNightFinds(morning);
        }
    }

    private void WakeUp()
    {
        if (LoadSaveIfNeeded())
        {
            Advance(CurrentDay());
            RestorePetShopDeliveries();
            RestoreCatItems();
            DeliverPendingCatNightFinds();
        }
    }

    private void OpenCatDay()
    {
        if (LoadSaveIfNeeded())
        {
            RestorePetShopDeliveries();
        }
        RestoreCatItems();
        DeliverPendingCatNightFinds();
    }

    private void Advance(int day)
    {
        Cattery state = cattery!;
        if (!state.StarterClaimed || day <= state.LastDay)
        {
            return;
        }

        IReadOnlyList<Cat> kittens = state.AdvanceTo(day);
        RememberBirthPlacements(kittens);
        DropPendingCatFur();
        ProcessCatFurProcessors();
        var outsideForagers = new HashSet<Cat>();
        var nightReport = new CatNightReport();
        var mealVisitors = ProcessHouseNight(day, outsideForagers, nightReport);
        SynchronizeCatItems(day, kittens);
        foreach (var litter in kittens.GroupBy(cat => cat.MotherId))
        {
            string mother = state.Cats.Find(cat => cat.Id == litter.Key)?.Name ?? CatText.T("母猫");
            int abandoned = litter.Count(cat => cat.WasAbandoned);
            string overflowNote = abandoned > 0 ? CatText.F("其中 {0} 只因空间不足被丢弃。", abandoned) : string.Empty;
            nightReport.Add(litter.Key, mother, CatText.F("生了 {0} 只幼猫。{1}", litter.Count(), overflowNote));
        }
        SettleCatNightActivity(day, mealVisitors, outsideForagers, nightReport);
        RestoreCatFood();
        SaveCattery();
        foreach (string line in nightReport.Lines())
            activeStore!.AddNightLog(line);
    }

    private void StockScanner(List<GameItem> stock)
    {
        if (HasCattery() && !stock.Any(item => item.identifier == GeneScannerId) &&
            !EmporiumEntry.Instance.GetAllOwnedItems().Any(item => item.identifier == GeneScannerId))
        {
            stock.Add(ModHelper.SpawnItem(GeneScannerId));
        }
    }

    private static string StageName(AgeStage stage) => stage switch
    {
        AgeStage.Kitten => CatText.T("幼猫"),
        AgeStage.Adult => CatText.T("成年猫"),
        AgeStage.Senior => CatText.T("老年猫"),
        _ => CatText.T("已离世")
    };

    private static string DescribeAppearance(Phenotype traits)
    {
        string coat = traits.FurLength switch
        {
            "hairless" => CatText.F("无毛 · {0}皮肤", ColorName(traits.SkinColor)),
            "long" => CatText.T("长毛 · ") + ColorName(traits.BaseColor),
            _ => CatText.T("短毛 · ") + ColorName(traits.BaseColor)
        };
        string pattern = traits.FurLength == "hairless" ? string.Empty : CoatPatternName(traits.CoatPattern);
        string marking = traits.FurLength == "hairless" ? string.Empty : CatText.T(CoatMarking(traits.Marking).Name);
        string eyes = traits.EyeLeft == traits.EyeRight
            ? CatText.F("{0}眼", ColorName(traits.EyeLeft))
            : CatText.F("{0}／{1}异瞳", ColorName(traits.EyeLeft), ColorName(traits.EyeRight));
        string ears = traits.EarShape switch
        {
            "folded" => CatText.T(" · 折耳"),
            "slight" => CatText.T(" · 微折耳"),
            _ => string.Empty
        };
        string nose = traits.NosePigment == "spotted" ? CatText.T(" · 鼻斑") : string.Empty;
        return coat + (pattern.Length > 0 ? " · " + pattern : "") +
            (marking.Length > 0 ? " · " + marking : "") + " · " + eyes + ears + nose;
    }

    private static string CoatPatternName(int pattern) => pattern switch
    {
        2 => CatText.T("渐层"), 3 => CatText.T("虎斑"), 4 => CatText.T("三花"), 5 => CatText.T("拼色"),
        6 => CatText.T("烟色"), 7 => CatText.T("银渐层"), 8 => CatText.T("银壳色"), _ => string.Empty
    };

    private static (string Name, int Price) CoatMarking(string marking) => marking switch
    {
        "gloves" => ("白手套", 4),
        "tuxedo" => ("乌云盖雪", 21),
        "bib" => ("白围兜", 3),
        "point" => ("重点色", 28),
        "van" => ("梵花", 10),
        "harlequin" => ("大块双色", 7),
        "spotted" => ("斑点", 2),
        "snowpaws" => ("踏雪寻梅", 29),
        "inktail" => ("雪里拖枪", 30),
        "inkseal" => ("挂印拖枪", 34),
        _ => (string.Empty, 0)
    };

    private static string ColorName(string color) => color switch
    {
        "ginger" => CatText.T("橘色"),
        "cream" => CatText.T("奶油色"),
        "black" => CatText.T("黑色"),
        "charcoal" => CatText.T("炭灰色"),
        "blue" => CatText.T("蓝色"),
        "chocolate" => CatText.T("巧克力色"),
        "cinnamon" => CatText.T("肉桂色"),
        "fawn" => CatText.T("浅棕色"),
        "lilac" => CatText.T("丁香色"),
        "silver" => CatText.T("银色"),
        "chalk" => CatText.T("白色"),
        "rose" => CatText.T("粉色"),
        "sand" => CatText.T("沙色"),
        "pearl" => CatText.T("珠白色"),
        "slate" => CatText.T("岩灰色"),
        "cocoa" => CatText.T("可可色"),
        "amber" => CatText.T("琥珀色"),
        "gold" => CatText.T("金色"),
        "copper" => CatText.T("铜色"),
        "hazel" => CatText.T("榛色"),
        "green" => CatText.T("绿色"),
        "aqua" => CatText.T("水蓝色"),
        _ => color
    };
}
