using System;
using System.Collections.Generic;
using System.Linq;

namespace DeepSpaceCattery;

public sealed partial class GameMod
{
    private const string MetalIngotId = "metal_ingot";
    private const string NurseryFabricId = "kotton_fabric";
    private const int HouseWidth = 4;
    private const int HouseHeight = 3;
    private const int NurseryWidth = 6;
    private const int NurseryInitialHeight = 4;
    private const int CompartmentMaxHeight = 10;

    private string HouseIdentityTag => modId + ".house_id";
    private string HouseNestWidthTag => modId + ".house_nest_width";
    private string HouseNestHeightTag => modId + ".house_nest_height";
    private string HouseNurseryHeightTag => modId + ".house_nursery_height";

    // Sizes stored in the run before individual houses had their own layout.
    private (int Width, int Height) HouseGridSize => cattery?.GetHouseSize() ??
        (Cattery.InitialHouseWidth, Cattery.InitialHouseHeight);

    private bool IsCatItem(GameItem item) => item.identifier == AdultCatId || item.identifier == KittenId;
    private bool IsHouse(GameItem item) => item.identifier == HouseId || item.identifier == PreviousHouseId;

    private string GetHouseIdentity(GameItem house)
    {
        string id = house.GetTagReadonly(HouseIdentityTag).GetString();
        if (string.IsNullOrEmpty(id))
        {
            id = Guid.NewGuid().ToString("N");
            WriteHouseIdentity(house, id);
        }
        return id;
    }

    private void WriteHouseIdentity(GameItem house, string id) =>
        house.ModifyTag(HouseIdentityTag, tag => tag.SetString(id));

    private bool HasHouseLayout(GameItem house) => house.GetTagReadonly(HouseNestWidthTag).GetInt() > 0;

    private (int Width, int Height, int NurseryHeight) ReadHouseLayout(GameItem house)
    {
        int width = house.GetTagReadonly(HouseNestWidthTag).GetInt();
        if (width <= 0)
        {
            var size = HouseGridSize;
            return (size.Width, size.Height, cattery?.NurseryHeight ?? 0);
        }
        return (width, house.GetTagReadonly(HouseNestHeightTag).GetInt(),
            house.GetTagReadonly(HouseNurseryHeightTag).GetInt());
    }

    private void WriteHouseLayout(GameItem house, int width, int height, int nurseryHeight)
    {
        house.ModifyTag(HouseNestWidthTag, tag => tag.SetInt(width));
        house.ModifyTag(HouseNestHeightTag, tag => tag.SetInt(height));
        house.ModifyTag(HouseNurseryHeightTag, tag => tag.SetInt(nurseryHeight));
    }

    private void CopyHouseLayout(GameItem source, GameItem destination)
    {
        var (width, height, nurseryHeight) = ReadHouseLayout(source);
        WriteHouseLayout(destination, width, height, nurseryHeight);
        WriteHouseIdentity(destination, GetHouseIdentity(source));
        ApplyHouseGridSizes(destination, width, height, nurseryHeight);
    }

    private static GameGridInventory? MainCompartment(GameItem house) =>
        MachineHelper.GetBodyStorage(house, 0) as GameGridInventory;

    private static GameGridInventory? NurseryGrid(GameItem house) =>
        MachineHelper.GetBodyStorage(house, 2) as GameGridInventory;

    private GameGridInventory? NurseryCompartment(GameItem house) =>
        ReadHouseLayout(house).NurseryHeight >= NurseryInitialHeight ? NurseryGrid(house) : null;

    private void AttachHouseInventory(GameItem house)
    {
        // Native loading restores child slots before it calls onLoaded, so provision room for saved upgrades first.
        bool decoding = SaveManager.IsDecoding;
        if (!decoding)
        {
            GetHouseIdentity(house);
            WriteHouseLayout(house, Cattery.InitialHouseWidth, Cattery.InitialHouseHeight, nurseryHeight: 0);
        }
        var main = new GameGridInventory(decoding ? 12 : Cattery.InitialHouseWidth,
            decoding ? CompartmentMaxHeight : Cattery.InitialHouseHeight);
        main.mayInventoryAddItemFunc = (item, _) => CanStoreInHouse(house, item);
        var nursery = new GameGridInventory(NurseryWidth, decoding ? CompartmentMaxHeight : NurseryInitialHeight);
        nursery.mayInventoryAddItemFunc = (item, _) => ReadHouseLayout(house).NurseryHeight >= NurseryInitialHeight &&
            GeneralHelper.IsItemOwned(house, item) && (IsCatItem(item) || IsHouseFood(item) || item.identifier == CatFurId);
        // Preserve the old assembly slot index so remaining cloth can be retrieved from older saves.
        var assembly = new GameSlotInventory { overrideLockInsert = true };

        MachineryPanel feederPanel = CreateFeederPanel(house, out System.Action refreshFeeder);
        MachineryPanel nurseryPanel = MachineHelper.Panel(CatText.T("繁育间"), nursery, "nursery");
        MachineryPanel assemblyPanel = MachineHelper.Panel(CatText.T("取出旧材料"), assembly, "nursery_material");
        // Keep the old main compartment and feeder as the first two saved inventories.
        PixelWindow window = MachineHelper.CreateMachineryWindow(true, new[] { 1, 0, 2, 3 }, new[]
        {
            MachineHelper.Panel(CatText.T("猫窝"), main, "cats"),
            feederPanel, nurseryPanel, assemblyPanel
        });
        house.SetContentWindow(window);
        house.EnableTag("CONTAINER_TAG");

        void RefreshHouse()
        {
            var (width, height, nurseryHeight) = ReadHouseLayout(house);
            ApplyHouseGridSizes(house, width, height, nurseryHeight);
            nurseryPanel.SetHidden(nurseryHeight < NurseryInitialHeight);
            assemblyPanel.SetHidden(assembly.childItem == null);
            refreshFeeder();
            window.Validate();
        }

        assembly.onSlotRemoveItemEndFunc = (_, _, _) => RefreshHouse();
        bool ApplyUpgrade(GameItem source, GameItem _)
        {
            if (!ConsumeOne(source))
            {
                return false;
            }
            var (width, height, nurseryHeight) = ReadHouseLayout(house);
            if (source.identifier == NurseryFabricId)
            {
                nurseryHeight = nurseryHeight < NurseryInitialHeight ? NurseryInitialHeight : nurseryHeight + 1;
            }
            else
            {
                height++;
            }
            WriteHouseLayout(house, width, height, nurseryHeight);
            RefreshHouse();
            return true;
        }

        NativeCatInteraction.Bind(house, IsHouseUpgradeMaterial,
            (source, _) => enabled && HasCattery() && (source.identifier switch
            {
                MetalIngotId => ReadHouseLayout(house).Height < CompartmentMaxHeight,
                NurseryFabricId => ReadHouseLayout(house).NurseryHeight < CompartmentMaxHeight,
                _ => false
            }), ApplyUpgrade);
        window.openCallback += RefreshHouse;
        house.onLoaded += _ =>
        {
            // A pre-upgrade save may not have loaded its cattery settings yet.
            if (HasHouseLayout(house))
            {
                RefreshHouse();
            }
        };
        if (!decoding)
        {
            RefreshHouse();
        }
        else
        {
            nurseryPanel.SetHidden(true);
            assemblyPanel.SetHidden(true);
        }
    }

    private static bool IsHouseUpgradeMaterial(GameItem item) =>
        item.identifier == MetalIngotId || item.identifier == NurseryFabricId;

    private void AddHouseUpgradeTooltip(RichTextBuilder builder, GameItem item)
    {
        if (!IsHouse(item))
        {
            return;
        }

        var (width, height, nurseryHeight) = ReadHouseLayout(item);
        builder.AddLine(" ");
        AddCompartmentUpgradeTooltip(builder, CatText.T("猫窝"), width, height, CatText.T("金属锭"));
        if (nurseryHeight < NurseryInitialHeight)
        {
            builder.AddLine(CatText.T("繁育间 · 未建造"));
            builder.AddLine(CatText.F("[使用韧棉织物建造（{0}×{1} 格）]", NurseryWidth, NurseryInitialHeight),
                color: RenderHandler.ColorPalette.LightBlue);
        }
        else
        {
            AddCompartmentUpgradeTooltip(builder, CatText.T("繁育间"), NurseryWidth, nurseryHeight, CatText.T("韧棉织物"));
        }
    }

    private static void AddCompartmentUpgradeTooltip(RichTextBuilder builder, string name, int width, int height,
        string material)
    {
        builder.AddLine(CatText.F("{0} · {1}×{2} 格", name, width, height));
        int remaining = CompartmentMaxHeight - height;
        if (remaining > 0)
        {
            builder.AddLine(CatText.F("[使用{0}扩建（剩余 {1} 次升级）]", material, remaining),
                color: RenderHandler.ColorPalette.LightBlue);
        }
        else
        {
            builder.AddLine(CatText.T("[已达最大尺寸]"), color: RenderHandler.ColorPalette.Cyan);
        }
    }

    private static void SetCompartmentSize(GameGridInventory inventory, int width, int height)
    {
        if (inventory.inventoryShape.width != width || inventory.inventoryShape.height != height)
        {
            inventory.SetShape(width, height);
        }
    }

    private static void ApplyHouseGridSizes(GameItem house, int width, int height, int nurseryHeight)
    {
        if (MainCompartment(house) is GameGridInventory main)
        {
            SetCompartmentSize(main, width, height);
        }
        if (NurseryGrid(house) is GameGridInventory nursery)
        {
            SetCompartmentSize(nursery, NurseryWidth, Math.Max(NurseryInitialHeight, nurseryHeight));
        }
    }

    private static bool ConsumeOne(GameItem item) =>
        item.unitCount > 0 && GraphUtils.TryDestroyAll(new List<GameItem> { item }, amount: 1) == 0;

    private GameItem[] FindHouses(EmporiumEntry entry)
    {
        GameItem[] houses = entry.GetAllOwnedItems(includeAfterhour: false).Where(IsHouse).Distinct()
            .Where(house => house.parentInventory != null && GeneralHelper.IsItemEffectivelyOwned(house) &&
                IsAccessibleStoreInventory(entry, house.parentInventory) && MainCompartment(house) != null)
            .ToArray();
        var identities = new HashSet<string>();
        foreach (GameItem house in houses)
        {
            string id = GetHouseIdentity(house);
            if (!identities.Add(id))
            {
                id = Guid.NewGuid().ToString("N");
                WriteHouseIdentity(house, id);
                identities.Add(id);
            }
            if (!HasHouseLayout(house) && cattery != null)
            {
                var (width, height, nurseryHeight) = ReadHouseLayout(house);
                WriteHouseLayout(house, width, height, nurseryHeight);
                ApplyHouseGridSizes(house, width, height, nurseryHeight);
            }
        }
        return houses;
    }

    private GameItem? FindHouse() => EmporiumEntry.Instance is EmporiumEntry entry ? FindHouses(entry).FirstOrDefault() : null;
}
