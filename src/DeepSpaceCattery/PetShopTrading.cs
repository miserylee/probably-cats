using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace DeepSpaceCattery;

public sealed partial class GameMod
{
    private const int PetShopSupplyPeriod = 7;
    private const int NightPetShopKittenCount = 6;
    private const float PetShopOwnerCounterOffset = 67f;
    private static GameMod? petShopTradeOwner;
    private PetShopSession? petShopNightSession;
    private PetShopSession? petShopDaySession;

    private sealed class PetShopSession
    {
        public PlayerStore Store { get; }
        public EmporiumEntry Entry { get; }
        public List<GameItem> Items { get; } = new();
        public Dictionary<GameItem, Cat> Cats { get; } = new();
        public Dictionary<GameItem, Cat?> Purchases { get; } = new();
        public List<(GameItem Item, GameGridInventory Counter, GridShape Shape)> HomeCounterItems { get; } = new();
        public bool BackCounterTransferred { get; set; }
        public CatPortraits? StockPortraits { get; set; }
        public Transform? ClerkTransform { get; set; }
        public Vector3 ClerkOriginalLocalPosition { get; set; }
        public (IdleAnimator Animator, bool WasEnabled)? ClerkIdle { get; set; }
        public (BoxCollider2D Collider, Vector2 Size, Vector2 Offset)? ClerkOriginalHitArea { get; set; }

        public PetShopSession(PlayerStore store, EmporiumEntry entry)
        {
            Store = store;
            Entry = entry;
        }
    }

    private void EnterPetShop()
    {
        LeavePetShop();
        if (!LoadSaveIfNeeded() || activeStore is not PlayerStore store ||
            EmporiumEntry.Instance is not EmporiumEntry entry || MapUIManager.Instance is not MapUIManager map)
        {
            return;
        }
        if (map.Current is DelegateAfterhourLocation location)
        {
            location.panelObject = map.UIUpgradeMerchantPanel;
        }

        var session = new PetShopSession(store, entry);
        petShopNightSession = session;
        BuildPetShopStock(session, forDayVisit: false);
        var clerk = CreatePetShopSeller("pet_shop_clerk",
            CatText.T("从右边挑好商品，拿到称重台结账。"));
        clerk.mainDialogue.NextDialogue().SetText(clerk.displayName, CatText.T("买好的东西我会送到你的店里。"));
        clerk.isMultiBuyDisabled = MayClaimFirstCat(cattery!);
        clerk.onItemSold += (item, _) => RecordNightPetShopPurchase(session, clerk, item);
        clerk.AddBasicDialog();
        clerk.CompleteClientCreation();

        store.playerLocation = PlayerStore.PlayerLocation.INVENTOR;
        StashPetShopCounter(session, entry.frontInvinvElement);
        if (entry.backInvinvElementCounter.childItems.Count == 0)
        {
            map.TransferBackInvToCounter();
            session.BackCounterTransferred = true;
        }
        else
        {
            StashPetShopCounter(session, entry.backInvinvElement);
        }
        entry.ExpandFrontCounterGrid();
        PlacePetShopStock(entry.frontInvinvElement, session.Items);
        entry.frontInvWindow.Show();
        entry.backInvWindow.Show();

        store.currentClientInstance = StoreClientInstance.CreateClientInstance(clerk);
        store.isClientArrived = true;
        if (StoreClientMono.Instance is StoreClientMono clerkView)
        {
            clerkView.image.sprite = SpriteDict.Instance.ShowClientSprite(clerk.GetSprite());
            clerkView.SetImmediatlyArrive();
            if (Camera.main is Camera camera)
            {
                // The sprite's waist meets the back edge of the weighing counter at y=284 in the 540-pixel scene.
                session.ClerkTransform = clerkView.transform;
                session.ClerkOriginalLocalPosition = clerkView.transform.localPosition;
                clerkView.transform.position -= Vector3.up * (camera.orthographicSize * 2f * PetShopOwnerCounterOffset / 540f);
                if (clerkView.GetComponent<BoxCollider2D>() is BoxCollider2D hitArea)
                {
                    session.ClerkOriginalHitArea = (hitArea, hitArea.size, hitArea.offset);
                    // Cover the visible bust through the tabletop edge in native sprite units.
                    hitArea.size = new Vector2(1.12f, 1.44f);
                    hitArea.offset = new Vector2(0f, -.335f);
                }
            }
            if (clerkView.GetComponent<IdleAnimator>() is IdleAnimator idle)
            {
                // Capture the countertop pose before disabling the animator, which resets to its saved pose.
                session.ClerkIdle = (idle, idle.enabled);
                idle.Freeze();
                idle.enabled = false;
            }
        }
        map.SetCreditDisplayPositionToDrJackson();
        StoreUIManager.Instance?.storeCreditDisplay?.creditDisplayText?.gameObject.SetActive(true);
    }

    private void LeavePetShop()
    {
        PetShopSession? session = petShopNightSession;
        petShopNightSession = null;
        if (session == null)
        {
            return;
        }
        if (session.ClerkTransform != null)
        {
            session.ClerkTransform.localPosition = session.ClerkOriginalLocalPosition;
        }
        if (session.ClerkIdle is { } idle && idle.Animator != null)
        {
            idle.Animator.enabled = idle.WasEnabled;
            idle.Animator.Freeze();
        }
        if (session.ClerkOriginalHitArea is { } hitArea && hitArea.Collider != null)
        {
            hitArea.Collider.size = hitArea.Size;
            hitArea.Collider.offset = hitArea.Offset;
        }
        if (!ReferenceEquals(session.Store, PlayerStore.Instance) ||
            !ReferenceEquals(session.Entry, EmporiumEntry.Instance))
        {
            session.StockPortraits?.Dispose();
            return;
        }

        NegociationUIManager.Instance?.CloseUI();
        foreach (GameItem purchase in session.Purchases.Keys)
        {
            purchase.parentInventory?.Expel(purchase);
        }
        ClearPetShopStock(session);
        EmporiumEntry entry = session.Entry;
        var belongings = new List<GameItem>();
        foreach (GameInventory inventory in new[] { entry.frontInvinvElement, entry.backInvinvElement, entry.afterhourInventory })
        {
            foreach (GameItem item in inventory.childItems.Where(GeneralHelper.IsItemOwned).ToArray())
            {
                item.CloseContentWindow(true);
                if (inventory.Expel(item))
                {
                    belongings.Add(item);
                }
            }
        }
        entry.ResetFrontCounterGrid();
        if (MapUIManager.Instance is MapUIManager map)
        {
            if (session.BackCounterTransferred)
            {
                map.TransferCounterToBackInv();
            }
            map.ResetCreditDisplayPosition();
            map.DeactivateClientOutsideStore();
        }
        StoreUIManager.Instance?.HideClient();
        session.Store.playerLocation = PlayerStore.PlayerLocation.IN_STORE;
        foreach (var (item, counter, shape) in session.HomeCounterItems)
        {
            item.modifiedShape = shape;
            if (!counter.UncheckedAccept(item))
            {
                DeliverPetShopPurchase(item, null);
            }
        }
        foreach (var (item, cat) in session.Purchases)
        {
            DeliverPetShopPurchase(item, cat);
        }
        foreach (GameItem item in belongings)
        {
            DeliverPetShopPurchase(item, null);
        }
    }

    private static void StashPetShopCounter(PetShopSession session, GameGridInventory counter)
    {
        foreach (GameItem item in counter.childItems.ToArray())
        {
            GridShape shape = item.modifiedShape.Clone();
            item.CloseContentWindow(true);
            if (counter.Expel(item))
            {
                session.HomeCounterItems.Add((item, counter, shape));
            }
        }
    }

    private void BuildPetShopStock(PetShopSession session, bool forDayVisit)
    {
        int day = CurrentDay();
        var previews = new Cattery { GenomeRules = cattery!.GenomeRules };
        bool freeAdoption = !forDayVisit && MayClaimFirstCat(cattery);
        session.StockPortraits = new CatPortraits(modId);
        int catCount = forDayVisit ? 2 : 2 + NightPetShopKittenCount;
        for (int index = 0; index < catCount; index++)
        {
            bool kitten = index >= 2;
            bool male = kitten ? random.Next(2) == 0 : index == 1;
            Cat cat = previews.AddFounder(day, random, male, kitten);
            GameItem item = ModHelper.SetItemOwnerShip(ModHelper.SpawnItem(kitten ? KittenId : AdultCatId), false);
            item.ModifyTag(CatIdTag, tag => tag.SetString(cat.Id));
            UpdateCatItem(item, cat, cat.GetStage(day), session.StockPortraits);
            if (freeAdoption)
            {
                item.SetValue(0);
            }
            session.Cats.Add(item, cat);
            session.Items.Add(item);
        }
        session.Items.AddRange(CreatePetShopGoods(forDayVisit));
        foreach (GameItem item in session.Items)
        {
            item.SetAmount(1);
            var mayStack = item.mayStackItemFunc;
            item.mayStackItemFunc = (self, other) => GeneralHelper.IsItemOwned(self) &&
                (mayStack?.Invoke(self, other) ?? true);
        }
    }

    private StoreClient CreatePetShopSeller(string id, string greeting)
    {
        // Keep trade open for restocking between orders. The seller buys no items.
        var seller = CreatePetShopOwner(id, StoreClient.ClientIntent.SELLNBUY);
        seller.clientBuyingTagList.Clear();
        seller.clientBuyingIdList.Clear();
        seller.clientBuyingTagList.Add(modId + ".supplier_no_buy");
        seller.nonShootable = true;
        seller.isBargainAvailable = false;
        seller.mainDialogue.SetText(seller.displayName, greeting);
        seller.clientItemFeatureBuying.Add(PetShopMarkupFeatureId);
        return seller;
    }

    private void RecordNightPetShopPurchase(PetShopSession session, StoreClient clerk, GameItem item)
    {
        if (!session.Items.Contains(item) || session.Purchases.ContainsKey(item))
        {
            return;
        }

        session.Cats.TryGetValue(item, out Cat? cat);
        session.Purchases.Add(item, cat);
        if (cat != null && clerk.isMultiBuyDisabled)
        {
            clerk.isMultiBuyDisabled = false;
            int day = CurrentDay();
            foreach (var (other, candidate) in session.Cats)
            {
                if (!session.Purchases.ContainsKey(other))
                {
                    other.SetValue(GetCatMarketValue(candidate, candidate.GetStage(day)));
                }
            }
        }
        PlacePetShopStock(session.Entry.frontInvinvElement, session.Items.Where(candidate => !session.Purchases.ContainsKey(candidate)));
    }

    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.MoveDealItemToInventory))]
    private static class DispatchNightPetShopPurchase
    {
        private static bool Prefix(PlayerStore __instance, GameItem __0)
        {
            var session = petShopTradeOwner?.petShopNightSession;
            if (session == null || !ReferenceEquals(session.Store, __instance) || !session.Purchases.ContainsKey(__0))
            {
                return true;
            }
            // The native payment and ownership transfer have finished. Free the weighing space for the next order.
            __0.parentInventory?.Expel(__0);
            return false;
        }
    }

    private void QueueWeeklyPetShopSupplier(StoreClientManager manager)
    {
        int week = CurrentDay() / PetShopSupplyPeriod;
        if (week <= 0 || !LoadSaveIfNeeded() || cattery!.LastPetShopSupplyWeek >= week)
        {
            return;
        }

        var seller = CreatePetShopSeller("pet_shop_supplier",
            CatText.T("这周带来的猫和用品都摆在柜台上了。"));
        seller.cooldown = 0;
        seller.OnArrival += OpenDayPetShopStock;
        seller.OnDismiss += CloseDayPetShopStock;
        seller.onItemSold += (item, _) =>
        {
            var session = petShopDaySession;
            if (session == null)
            {
                return;
            }
            if (session.Cats.Remove(item, out Cat cat))
            {
                FinishPetShopAdoption(cat, item);
            }
            PlacePetShopStock(session.Entry.frontInvinvElement, session.Items, prioritizeStockOrder: true);
        };
        AddPetShopVisitor(manager, seller);
        if (manager.clientStack.Contains(seller))
        {
            cattery!.LastPetShopSupplyWeek = week;
            SaveCattery();
        }
    }

    private void OpenDayPetShopStock()
    {
        CloseDayPetShopStock();
        if (!LoadSaveIfNeeded() || activeStore is not PlayerStore store || EmporiumEntry.Instance is not EmporiumEntry entry)
        {
            return;
        }
        var session = new PetShopSession(store, entry);
        petShopDaySession = session;
        BuildPetShopStock(session, forDayVisit: true);
        PlacePetShopStock(entry.frontInvinvElement, session.Items, prioritizeStockOrder: true);
    }

    private void CloseDayPetShopStock()
    {
        PetShopSession? session = petShopDaySession;
        petShopDaySession = null;
        if (session != null && ReferenceEquals(session.Entry, EmporiumEntry.Instance) &&
            ReferenceEquals(session.Store, PlayerStore.Instance))
        {
            ClearPetShopStock(session);
        }
        else
        {
            session?.StockPortraits?.Dispose();
        }
    }

    private static void ClearPetShopStock(PetShopSession session)
    {
        foreach (GameItem item in session.Items)
        {
            if (!GeneralHelper.IsItemOwned(item) && !session.Purchases.ContainsKey(item))
            {
                item.Destroy();
            }
        }
        session.StockPortraits?.Dispose();
    }
}
