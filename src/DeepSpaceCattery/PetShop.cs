using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DeepSpaceCattery;

public sealed partial class GameMod
{
    private const int PetShopVisitorChance = 25;
    private const string LivestockFeederId = "livestock_feed_dispenser";
    private static string PetShopOwnerName => CatText.T("宠物店老板");
    private const string PetShopOwnerArt = "pet_shop_owner";

    private readonly Dictionary<GameItem, Cat> catsForCurrentBuyer = new();
    private Sprite? petShopOwnerSprite;
    private int petShopVisitorDay = int.MinValue;
    private int pendingShowcaseGuestDay = int.MinValue;
    private string CatSaleTag => modId + ".cat_on_offer";

    private bool MayClaimFirstCat(Cattery state) => !state.StarterClaimed && state.Cats.Count == 0 &&
        petShopNightSession?.Purchases.Values.Any(cat => cat != null) != true && !HasPendingPetShopAdoption();

    private void FinishPetShopAdoption(Cat adopted, GameItem item)
    {
        Cattery state = cattery!;
        adopted.HasPlacedItem = true;
        state.Cats.Add(adopted);
        state.StarterClaimed = true;
        if (state.LastDay == -1)
        {
            state.LastDay = CurrentDay();
        }
        catItems[adopted.Id] = item;
        UpdateCatItem(item, adopted, adopted.GetStage(CurrentDay()));
        SaveCattery();
    }

    private void QueuePetShopVisitor(StoreClientManager manager)
    {
        int day = CurrentDay();
        if (!LoadSaveIfNeeded() || petShopVisitorDay == day)
        {
            return;
        }
        petShopVisitorDay = day;
        if (MayClaimFirstCat(cattery!))
        {
            if (random.Next(100) >= PetShopVisitorChance)
            {
                return;
            }

            var boss = CreatePetShopOwner("shop_owner", StoreClient.ClientIntent.DIALOGUE);
            boss.mainDialogue.SetText(boss.displayName, CatText.T("我带了一只猫来。要是柜台上有地方，就留下它陪你看店吧。"))
                .SetEndAction(GiftFirstCatAtCounter);
            AddPetShopVisitor(manager, boss);
            return;
        }

        var offeredCats = FindCatSaleCandidates().ToArray();
        if (offeredCats.Length == 0)
        {
            return;
        }

        var showcaseAges = offeredCats.Where(offer => IsShowcaseCat(offer.Cat, offer.Item, day))
            .Select(offer => offer.Cat.GetStage(day)).ToArray();
        bool hasShowcaseCat = showcaseAges.Length > 0;
        if (!hasShowcaseCat && random.Next(100) >= PetShopVisitorChance)
        {
            return;
        }

        CatBuyerPreference preference = ChooseCatBuyerPreference(offeredCats, day, showcaseAges);
        var buyer = CreatePetShopVisitor("cat_buyer", preference.BuyerName ?? CatText.T("猫咪爱好者"), StoreClient.ClientIntent.BUY);
        buyer.clientBuyingTagList.Clear();
        buyer.clientBuyingIdList.Clear();
        buyer.clientBuyingTagList.Add(CatSaleTag);
        // Match the vanilla household buyer CreateCitizen, including native store budget modifiers.
        buyer.SetBudget(145 + RNG.GetRandomInt(0, 50) + day / 4);
        buyer.cooldown = 0;
        var lines = new List<string>();
        if (hasShowcaseCat)
        {
            lines.Add(CatText.T("我路过时看到了展柜里的猫。"));
            pendingShowcaseGuestDay = day;
        }
        if (!string.IsNullOrEmpty(preference.Introduction))
        {
            lines.Add(preference.Introduction);
        }
        if (preference.PurchaseRestriction is string restriction)
        {
            lines.Add(CatText.F("我{0}。", restriction));
            if (preference.ExtraPreferences is string extra)
            {
                lines.Add(CatText.F("有{0}这些特点就更好了。", extra));
            }
        }
        else
        {
            lines.Add(CatText.F("我想找{0}。", preference.Description));
        }

        Dialogue dialogue = buyer.mainDialogue.SetText(buyer.displayName, lines[0]);
        for (int index = 1; index < lines.Count; index++)
        {
            dialogue = dialogue.NextDialogue().SetText(buyer.displayName, lines[index]);
        }
        buyer.OnArrival += () => OfferCatsToVisitor(preference);
        buyer.OnDismiss += RestoreCatSaleProtection;
        buyer.onItemBought += (item, _) => FinishCatSale(item);
        AddPetShopVisitor(manager, buyer);
    }

    private void QueueShowcaseGuestAfterNormalClients(StoreClientManager manager)
    {
        if (pendingShowcaseGuestDay != CurrentDay())
        {
            return;
        }
        pendingShowcaseGuestDay = int.MinValue;
        QueueShowcaseOrdinaryVisitor(manager);
    }

    private static bool IsShowcaseCat(Cat cat, GameItem item, int day) =>
        EmporiumEntry.Instance?.showcaseElement is GameGridInventory showcase && item.parentInventory == showcase &&
        GeneralHelper.IsItemEffectivelyOwned(item) && !item.IsTag(GeneralHelper.JUST_SOLD_TAG) &&
        cat.IsInCare && cat.GetStage(day) != AgeStage.Dead;

    private StoreClient CreatePetShopOwner(string id, StoreClient.ClientIntent intent)
    {
        var spriteDictionary = SpriteDict.Instance?.spriteDictionary;
        string spriteName = SpriteDict.GetClerkSpriteName();
        if (spriteDictionary != null && GetPetShopOwnerSprite() is Sprite sprite)
        {
            spriteName = SpriteKey(PetShopOwnerArt);
            spriteDictionary[spriteName] = sprite;
        }
        return CreatePetShopVisitor(id, PetShopOwnerName, intent, spriteName);
    }

    private Sprite? GetPetShopOwnerSprite()
    {
        if (petShopOwnerSprite != null)
            return petShopOwnerSprite;

        // The native renderer uses a 288×211 canvas at 100 PPU with a centered pivot.
        const float nativeWidth = 288f;
        const float nativeHeight = 211f;
        LoadedMod? mod = ModLoader.Mods.FirstOrDefault(candidate =>
            string.Equals(candidate?.Manifest?.ID, modId, StringComparison.Ordinal));
        if (mod == null || !mod.Sprites.TryGetValue(PetShopOwnerArt, out Sprite source) ||
            source.rect.width * nativeHeight != source.rect.height * nativeWidth)
        {
            log.Log("宠物店老板立绘未加载，或画布比例与原版人物不一致。");
            return null;
        }

        source.texture.filterMode = FilterMode.Bilinear;
        source.texture.wrapMode = TextureWrapMode.Clamp;
        float pixelsPerUnit = 100f * source.rect.width / nativeWidth;
        petShopOwnerSprite = Sprite.Create(source.texture, source.rect, new Vector2(.5f, .5f),
            pixelsPerUnit, 0, SpriteMeshType.FullRect);
        return petShopOwnerSprite;
    }

    private StoreClient CreatePetShopVisitor(string id, string displayName, StoreClient.ClientIntent intent,
        string? spriteName = null)
    {
        var visitor = new StoreClient
        {
            identifier = modId + "." + id,
            displayName = displayName,
            spriteName = spriteName ?? SpriteDict.GetRandomLowerLevelSpriteName(),
            clientFaction = StoreClient.FACTION_LOWER,
            clientIntent = intent
        };
        StoreClientFactionSetup.InitLowerLevelCitizen(visitor);
        return visitor;
    }

    private static void AddPetShopVisitor(StoreClientManager manager, StoreClient visitor)
    {
        visitor.AddBasicDialog();
        visitor.CompleteClientCreation();
        manager.TryAddClient(visitor);
    }

    private void GiftFirstCatAtCounter()
    {
        if (!LoadSaveIfNeeded() || !MayClaimFirstCat(cattery!))
        {
            return;
        }

        GameItem item = ModHelper.SetItemOwnerShip(ModHelper.SpawnItem(AdultCatId), true);
        var previews = new Cattery { GenomeRules = cattery!.GenomeRules };
        Cat gift = previews.AddFounder(CurrentDay(), random, random.Next(2) == 0);
        item.ModifyTag(CatIdTag, tag => tag.SetString(gift.Id));
        if (TryPlaceIn(item, EmporiumEntry.Instance.frontInvinvElement))
        {
            FinishPetShopAdoption(gift, item);
        }
        else
        {
            item.Destroy();
        }
    }

    private IEnumerable<(Cat Cat, GameItem Item)> FindCatSaleCandidates()
    {
        int day = CurrentDay();
        foreach (Cat cat in cattery!.Cats)
        {
            if (cat.IsInCare && cat.GetStage(day) != AgeStage.Dead &&
                catItems.TryGetValue(cat.Id, out GameItem item) && item.parentInventory != null &&
                !item.IsTag(GeneralHelper.JUST_SOLD_TAG) && GeneralHelper.IsItemEffectivelyOwned(item))
            {
                yield return (cat, item);
            }
        }
    }

    private void OfferCatsToVisitor(CatBuyerPreference preference)
    {
        RestoreCatSaleProtection();
        activeCatBuyerPreference = preference;
        int day = CurrentDay();
        foreach (var (cat, item) in FindCatSaleCandidates())
        {
            AgeStage stage = cat.GetStage(day);
            if (preference.Accepts(cat, stage))
            {
                catsForCurrentBuyer.Add(item, cat);
            }
            SetCatMarketTraits(item, cat, stage);
        }
    }

    private void FinishCatSale(GameItem item)
    {
        if (!catsForCurrentBuyer.TryGetValue(item, out Cat cat) || !cat.IsInCare)
        {
            return;
        }
        cat.WasSold = true;
        catItems.Remove(cat.Id);
        catsForCurrentBuyer.Remove(item);
        item.DisableTag(CatSaleTag);
        item.EnableTag(ProtectedFromSale);
        SaveCattery();
    }

    private void RestoreCatSaleProtection()
    {
        activeCatBuyerPreference = null;
        var previousOffers = catsForCurrentBuyer.ToArray();
        catsForCurrentBuyer.Clear();
        int day = CurrentDay();
        foreach (var (item, cat) in previousOffers)
        {
            SetCatMarketTraits(item, cat, cat.GetStage(day));
        }
    }

    private void ResetCatSaleVisitor()
    {
        RestoreCatSaleProtection();
        petShopVisitorDay = int.MinValue;
        pendingShowcaseGuestDay = int.MinValue;
    }
}
