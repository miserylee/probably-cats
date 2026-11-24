using System;
using UnityEngine;

namespace DeepSpaceCattery;

public sealed partial class GameMod
{
    // The native inventory uses 16 pixels per grid cell at 100 pixels per unit.
    private const int InventoryArtScale = 4;
    private const int NativeGridPixels = 16;
    private const float InventoryArtPixelsPerUnit = 100f * InventoryArtScale;

    private static readonly (string Key, int Width, int Height, float PixelsPerUnit)[] InventoryArt =
    {
        ("cattery_house", 4, 3, InventoryArtPixelsPerUnit),
        ("cattery_cat_food", 1, 1, InventoryArtPixelsPerUnit / CatFoodSize),
        ("cattery_cat_fur", 1, 1, InventoryArtPixelsPerUnit),
        ("cattery_fur_processor", 2, 2, InventoryArtPixelsPerUnit * 2 / 3)
    };

    private void LoadInventoryArt(LoadedMod mod)
    {
        if (!string.Equals(mod.Manifest.ID, modId, StringComparison.Ordinal))
        {
            return;
        }

        foreach (var (key, width, height, pixelsPerUnit) in InventoryArt)
        {
            if (!mod.Sprites.TryGetValue(key, out Sprite source) ||
                source.rect.width != width * NativeGridPixels * InventoryArtScale ||
                source.rect.height != height * NativeGridPixels * InventoryArtScale)
            {
                log.Log($"物品图 {key} 的源尺寸不匹配。");
                continue;
            }

            source.texture.filterMode = FilterMode.Bilinear;
            source.texture.wrapMode = TextureWrapMode.Clamp;
            if (source.pixelsPerUnit != pixelsPerUnit)
            {
                mod.Sprites[key] = Sprite.Create(source.texture, source.rect, new Vector2(.5f, .5f),
                    pixelsPerUnit, 0, SpriteMeshType.FullRect);
            }
        }

    }
}
