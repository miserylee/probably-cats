using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DeepSpaceCattery;

/// <summary>Colors the front portrait from the same model used by the art previews.</summary>
internal sealed class CatPortraits : IDisposable
{
    private const int Cell = 48;
    private const int Width = 2 * Cell;
    private const int Columns = 4;
    private const int RowsPerEarShape = 3;
    private const int EarShapeCount = 3;
    private const int Neutral = 120;
    private const float CoefficientScale = 160f;
    // Inventory UI sets native size at 100 reference PPU. There are 48
    // art pixels for every 16 logical pixels, so a portrait uses 300 PPU.
    private const float PixelsPerUnit = 300f;

    private readonly string modId;
    private readonly CatPortraitCoat coat = new();
    private readonly CatPortraitFace face = new();
    private readonly CatPortraitShape shape = new();
    private readonly Dictionary<bool, (Color32[] Reference, Color32[] Materials, int Rows)> atlases = new();
    private readonly Dictionary<(string CatId, bool Kitten), (Sprite Sprite, Texture2D Texture)> portraits = new();
    private readonly Dictionary<string, Sprite> registeredSprites = new();

    public CatPortraits(string modId) => this.modId = modId;

    public Sprite? Get(Cat cat, AgeStage stage)
    {
        if (stage == AgeStage.Dead || string.IsNullOrEmpty(cat.Id))
            return null;

        bool kitten = stage == AgeStage.Kitten;
        var key = (cat.Id, kitten);
        if (portraits.TryGetValue(key, out var cached))
            return cached.Sprite;

        int height = kitten ? Cell : 4 * Cell;
        if (FindMod() is not LoadedMod mod || !TryGetAtlas(mod, kitten, height, out var atlas))
            return null;

        Phenotype phenotype = cat.GetPhenotype();
        int coatPreset = phenotype.CoatPattern >= 6 ? 0 : Math.Clamp(phenotype.CoatPattern - 1, 0, 4);
        int preset = phenotype.FurLength switch
        {
            "hairless" => 10,
            "long" => 5 + coatPreset,
            _ => coatPreset
        };
        Color32 main = phenotype.FurLength == "hairless" ? Skin(phenotype.SkinColor) : Coat(phenotype.BaseColor);
        Color32 patch = Coat(phenotype.PatchColor);
        Color32 left = Eye(phenotype.EyeLeft);
        Color32 right = Eye(phenotype.EyeRight);
        var colors = new Color32[Width * height];
        int firstColumn = preset % Columns * Width;
        int earShapeIndex = atlas.Rows == RowsPerEarShape ? 0 : phenotype.EarShape switch
        {
            "slight" => 1,
            "folded" => 2,
            _ => 0
        };
        // PNG atlas rows were baked from the top; Unity's first scanline is the bottom.
        int firstRow = (atlas.Rows - 1 - earShapeIndex * RowsPerEarShape - preset / Columns) * height;
        for (int y = 0; y < height; y++)
        {
            int atlasIndex = (firstRow + y) * Columns * Width + firstColumn;
            int outputIndex = y * Width;
            for (int x = 0; x < Width; x++)
            {
                Color32 neutral = atlas.Reference[atlasIndex + x];
                if (neutral.a < 8)
                    continue;
                Color32 weight = atlas.Materials[atlasIndex + x];
                colors[outputIndex + x] = new Color32(
                    Channel(neutral.r, weight, main.r, patch.r, left.r, right.r),
                    Channel(neutral.g, weight, main.g, patch.g, left.g, right.g),
                    Channel(neutral.b, weight, main.b, patch.b, left.b, right.b),
                    neutral.a);
            }
        }
        coat.Apply(mod, colors, Width, height, kitten, earShapeIndex, phenotype, main, patch);
        face.Apply(colors, height, earShapeIndex, phenotype, mod);
        shape.Apply(colors, Width, height, phenotype, kitten, mod);
        ExtendEdgeColors(colors, height);

        var texture = new Texture2D(Width, height, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        texture.SetPixels32(colors);
        // The native inventory Image samples the sprite's alpha for pointer hits.
        texture.Apply(false, false);
        var sprite = Sprite.Create(texture, new Rect(0, 0, Width, height),
            new Vector2(0.5f, 0.5f), PixelsPerUnit, 0, SpriteMeshType.FullRect);
        portraits.Add(key, (sprite, texture));
        return sprite;
    }

    /// <summary>Registers a key that the game's native mod item-sprite lookup can resolve.</summary>
    public string? RegisterItemSprite(Cat cat, AgeStage stage)
    {
        Sprite? sprite = Get(cat, stage);
        LoadedMod? mod = FindMod();
        if (sprite == null || mod == null)
            return null;
        string key = "cattery_portrait_runtime_" + cat.Id + (stage == AgeStage.Kitten ? "_kitten" : "_adult");
        mod.Sprites[key] = sprite;
        registeredSprites[key] = sprite;
        return key;
    }

    public void Dispose()
    {
        LoadedMod? mod = FindMod();
        if (mod != null)
        {
            foreach (var entry in registeredSprites)
            {
                if (mod.Sprites.TryGetValue(entry.Key, out Sprite sprite) && ReferenceEquals(sprite, entry.Value))
                    mod.Sprites.Remove(entry.Key);
            }
        }

        foreach (var portrait in portraits.Values)
        {
            UnityEngine.Object.Destroy(portrait.Sprite);
            UnityEngine.Object.Destroy(portrait.Texture);
        }
        registeredSprites.Clear();
        portraits.Clear();
        atlases.Clear();
        coat.Clear();
        face.Clear();
        shape.Clear();
    }

    private LoadedMod? FindMod() =>
        ModLoader.Mods.FirstOrDefault(mod => string.Equals(mod?.Manifest?.ID, modId, StringComparison.Ordinal));

    private bool TryGetAtlas(LoadedMod mod, bool kitten, int height,
        out (Color32[] Reference, Color32[] Materials, int Rows) atlas)
    {
        if (atlases.TryGetValue(kitten, out atlas))
            return true;

        string prefix = kitten ? "cattery_portrait_kitten_" : "cattery_portrait_adult_";
        if (!mod.Sprites.TryGetValue(prefix + "reference", out Sprite reference)
            || !mod.Sprites.TryGetValue(prefix + "materials", out Sprite materials))
            return false;

        Texture2D referenceTexture = reference.texture;
        Texture2D materialTexture = materials.texture;
        if (referenceTexture.width != Columns * Width
            || (referenceTexture.height != RowsPerEarShape * height &&
                referenceTexture.height != RowsPerEarShape * EarShapeCount * height)
            || materialTexture.width != referenceTexture.width || materialTexture.height != referenceTexture.height
            || !referenceTexture.isReadable || !materialTexture.isReadable)
            return false;

        atlas = (referenceTexture.GetPixels32(), materialTexture.GetPixels32(), referenceTexture.height / height);
        atlases.Add(kitten, atlas);
        return true;
    }

    private static byte Channel(byte neutral, Color32 material, byte main, byte patch, byte left, byte right)
    {
        int difference = material.r * (main - Neutral) + material.g * (patch - Neutral)
            + material.b * (left - Neutral) + material.a * (right - Neutral);
        return (byte)Math.Clamp(Mathf.RoundToInt(neutral + difference / CoefficientScale), 0, 255);
    }

    // Preserve color immediately beyond the transparent silhouette, where an
    // inventory downsample may interpolate across the alpha ramp.
    private static void ExtendEdgeColors(Color32[] colors, int height)
    {
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int target = y * Width + x;
                if (colors[target].a != 0)
                    continue;
                Color32 next = default;
                if (x > 0 && colors[target - 1].a > next.a)
                    next = colors[target - 1];
                if (x + 1 < Width && colors[target + 1].a > next.a)
                    next = colors[target + 1];
                if (y > 0 && colors[target - Width].a > next.a)
                    next = colors[target - Width];
                if (y + 1 < height && colors[target + Width].a > next.a)
                    next = colors[target + Width];
                next.a = 0;
                colors[target] = next;
            }
        }
    }

    private static Color32 Coat(string color) => Rgb(color switch
    {
        "cream" => 0xE7CB9E, "chalk" => 0xF3ECE0, "fawn" => 0xC1A18C, "cinnamon" => 0xA96242,
        "chocolate" => 0x795245, "blue" => 0x7E8997, "silver" => 0xC1C5CA, "lilac" => 0xAE9CA5,
        "charcoal" => 0x403D48, "black" => 0x292831,
        _ => 0xC8773E
    });

    private static Color32 Skin(string color) => Rgb(color switch
    {
        "sand" => 0xC69C83, "pearl" => 0xECD1C8, "slate" => 0x938A99, "cocoa" => 0x936E64,
        _ => 0xD49F99
    });

    private static Color32 Eye(string color) => Rgb(color switch
    {
        "gold" => 0xEAC965, "copper" => 0xC27B4F, "hazel" => 0xAE9763, "green" => 0x90AE70,
        "aqua" => 0x79B6AA, "blue" => 0x7EAFD0,
        _ => 0xE4A24B
    });

    private static Color32 Rgb(int hex) => new((byte)(hex >> 16), (byte)(hex >> 8), (byte)hex, 255);
}

