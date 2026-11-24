using System;
using System.Collections.Generic;
using UnityEngine;

namespace DeepSpaceCattery;

/// <summary>Applies individual pigmentation and brow shape to registered facial surfaces.</summary>
internal sealed class CatPortraitFace
{
    private const int Width = 96;
    private const int EarShapes = 3;
    private const int FurTypes = 3;
    private const int Stride = Width * EarShapes * 3;

    private readonly Dictionary<int, Color32[]> mapsByHeight = new();

    public void Apply(Color32[] portrait, int height, int earShape, Phenotype traits, LoadedMod? mod)
    {
        int fur = traits.FurLength switch { "long" => 1, "hairless" => 2, _ => 0 };
        bool hairless = fur == 2;
        float autoShade = hairless ? traits.SkinColor switch
        {
            "slate" => .65f,
            "cocoa" => .45f,
            _ => 0f
        } : traits.BaseColor switch
        {
            "black" => 1f,
            "charcoal" => .87f,
            "chocolate" => .51f,
            "blue" => .42f,
            "silver" => .15f,
            _ => 0f
        };
        float earDarkness = traits.EarPigment switch
        {
            "rose" => 0f,
            "smoke" => .67f,
            "dark" => 1f,
            _ => autoShade
        };
        float noseDarkness = traits.NosePigment switch
        {
            "rose" or "spotted" => 0f,
            "dark" => 1f,
            _ => autoShade
        };
        bool roseEar = traits.EarPigment == "rose";
        bool spotted = traits.NosePigment == "spotted";
        bool roseNose = spotted || traits.NosePigment == "rose";
        if (traits.BrowType is "none" or "original" && !roseEar && !roseNose && earDarkness == 0f && noseDarkness == 0f)
        {
            return;
        }

        Color32[]? maps = ReadMaps(height, mod);
        if (maps == null)
        {
            return;
        }
        int firstRow = (FurTypes - 1 - fur) * height;
        int firstColumn = earShape * Width;
        float noseSeed = Mathf.Round((float)traits.NoseSeed * 65535f);
        float fleckA = Hash(noseSeed, 17, 13);
        float fleckB = Hash(noseSeed, 5, 29);

        for (int y = 0; y < height; y++)
        {
            int mapRow = (firstRow + y) * Stride + firstColumn;
            int row = y * Width;
            for (int x = 0; x < Width; x++)
            {
                int pixelIndex = row + x;
                Color32 pixel = portrait[pixelIndex];
                if (pixel.a == 0)
                {
                    continue;
                }

                int mapIndex = mapRow + x;
                pixel = DrawBrow(pixel, maps[mapIndex + EarShapes * Width], traits.BrowType, hairless);
                Color32 leather = maps[mapIndex];
                float relief = leather.b / Math.Max(1f, Math.Max(leather.r, leather.g));
                if (leather.r > 0 && (roseEar || earDarkness > 0f))
                {
                    Color light = roseEar ? EarRose(relief) : (Color)pixel;
                    pixel = Tint(pixel, Color.Lerp(light, EarDark(relief), earDarkness), leather.r / 255f);
                }
                if (leather.g > 0 && (roseNose || noseDarkness > 0f))
                {
                    float darkness = noseDarkness;
                    if (spotted)
                    {
                        Color32 coordinate = maps[mapIndex + 2 * EarShapes * Width];
                        float noseX = (coordinate.r / 255f - .5f) * 4f * (fleckA > .5f ? -1f : 1f);
                        float noseY = (coordinate.g / 255f - .5f) * 4f;
                        darkness = Math.Max(
                            Fleck(noseX, noseY, -.61f + .43f * fleckB, -.27f + .28f * fleckA,
                                .34f + .23f * fleckA, .38f + .18f * fleckB),
                            Fleck(noseX, noseY, .19f + .33f * fleckA, -.06f + .34f * fleckB,
                                .11f + .14f * fleckB, .15f + .08f * fleckA));
                    }
                    Color light = roseNose ? NoseRose(relief) : (Color)pixel;
                    pixel = Tint(pixel, Color.Lerp(light, NoseDark(relief), darkness), leather.g / 255f);
                }
                portrait[pixelIndex] = pixel;
            }
        }
    }

    public void Clear() => mapsByHeight.Clear();

    private Color32[]? ReadMaps(int height, LoadedMod? mod)
    {
        if (mapsByHeight.TryGetValue(height, out Color32[]? cached))
        {
            return cached;
        }
        string name = height == 48 ? "cattery_portrait_kitten_face" : "cattery_portrait_adult_face";
        if (mod == null || !mod.Sprites.TryGetValue(name, out Sprite? sprite) || sprite == null || !sprite.texture.isReadable ||
            sprite.texture.width != Stride || sprite.texture.height != FurTypes * height)
        {
            return null;
        }
        Color32[] maps = sprite.texture.GetPixels32();
        mapsByHeight.Add(height, maps);
        return maps;
    }

    private static Color32 DrawBrow(Color32 pixel, Color32 chart, string style, bool hairless)
    {
        if (chart.b == 0 || style is "none" or "original")
        {
            return pixel;
        }
        float widthwise = chart.r / (float)chart.b;
        if (widthwise <= 0f || widthwise >= 1f)
        {
            return pixel;
        }
        float offset = (chart.g / (float)chart.b - .5f) * 100f;
        float weight = chart.b / 255f;
        var color = (Color)pixel;
        if (style == "points")
        {
            float u = (widthwise - .32f) / .146f;
            float v = (offset + 1.8f + 2.8f * u) / 5f;
            float edge = Math.Max(.88f * Math.Abs(u) + .36f * Math.Abs(v), .36f * Math.Abs(u) + .87f * Math.Abs(v));
            float coverage = 1f - Smooth(.75f, 1.25f, edge);
            float lightness = .299f * color.r + .587f * color.g + .114f * color.b;
            Color accent = Color.Lerp(color * .72f, color * .81f + new Color(.24f, .195f, .145f),
                1f - Smooth(.63f, .85f, lightness));
            return Tint(pixel, accent, coverage * weight * (hairless ? .26f : .95f));
        }

        float line = Mathf.Sin(Mathf.PI * widthwise);
        float halfWidth;
        float strength;
        switch (style)
        {
            case "fine":
                halfWidth = 2.25f * line * (1f - .24f * widthwise);
                offset += 1.6f * line;
                strength = .74f;
                break;
            case "bold":
                halfWidth = 6.2f * Mathf.Pow(line, .65f) * (1f - .58f * widthwise);
                offset += 1.7f * line;
                strength = .87f;
                break;
            case "arched":
                halfWidth = 3.6f * Mathf.Pow(line, .76f) * (1f - .43f * widthwise);
                offset += 8.2f * line;
                strength = .82f;
                break;
            default:
                return pixel;
        }
        if (hairless)
        {
            halfWidth = Math.Max(.6f, .55f * halfWidth);
            float recess = (offset - .45f * halfWidth) / 1.6f;
            float ridge = (offset + halfWidth) / 2.3f;
            float relief = .115f * Mathf.Exp(-ridge * ridge) - .17f * Mathf.Exp(-recess * recess);
            return Tint(pixel, color * (1f + relief * line * strength), weight);
        }
        float ink = 1f - Smooth(Math.Max(0f, halfWidth - 1.15f), halfWidth + 1.45f, Math.Abs(offset));
        return Tint(pixel, new Color(color.r * .37f, color.g * .31f, color.b * .34f), ink * strength * weight);
    }

    private static float Fleck(float x, float y, float centerX, float centerY, float radiusX, float radiusY)
    {
        float u = (x - centerX) / radiusX;
        float v = (y - centerY) / radiusY;
        float edge = Mathf.Sqrt(u * u + v * v) + .075f * Mathf.Sin(u * 4f + v * 2f) * Mathf.Sin(v * 3.5f);
        return 1f - Smooth(.72f, 1.28f, edge);
    }

    private static float Hash(float seed, float y, float z)
    {
        float qx = Fract(seed * .1031f);
        float qy = Fract(y * .1030f);
        float qz = Fract(z * .0973f);
        float dot = qx * (qy + 33.33f) + qy * (qx + 33.33f) + qz * (qz + 33.33f);
        return Fract((qx + qy + 2f * dot) * (qz + dot));
    }

    private static float Fract(float value) => value - Mathf.Floor(value);

    private static float Smooth(float start, float end, float value)
    {
        float t = Mathf.Clamp01((value - start) / (end - start));
        return t * t * (3f - 2f * t);
    }

    private static Color EarRose(float relief) => Color.Lerp(new Color(.31f, .14f, .16f), new Color(.94f, .65f, .58f), relief);
    private static Color NoseRose(float relief) => Color.Lerp(new Color(.34f, .13f, .16f), new Color(.95f, .59f, .56f), relief);
    private static Color EarDark(float relief) => Color.Lerp(new Color(.055f, .051f, .062f), new Color(.245f, .227f, .241f), relief);
    private static Color NoseDark(float relief) => Color.Lerp(new Color(.037f, .031f, .039f), new Color(.225f, .198f, .217f), relief);

    private static Color32 Tint(Color32 pixel, Color target, float weight)
    {
        Color32 color = (Color32)Color.Lerp(pixel, target, weight);
        color.a = pixel.a;
        return color;
    }
}
