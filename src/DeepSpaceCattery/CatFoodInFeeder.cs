using System;
using System.Reflection;
using HarmonyLib;

namespace DeepSpaceCattery;

internal static class CatFoodInFeeder
{
    private const string StoredCatFoodTag = "deep_space_cattery.feeder_cat_food_kcal";

    internal static string? FoodTag { get; set; }

    internal static int GetTrackedCalories(GameItem feeder) =>
        Math.Clamp(feeder.GetTagReadonly(StoredCatFoodTag).GetInt(), 0, GetOtherCalories(feeder));

    private static int GetOtherCalories(GameItem feeder) =>
        Math.Max(0, feeder.GetTagReadonly(MachineFeedDispenser.STORED_OTHER_KEY).GetInt());

    private static void SetTrackedCalories(GameItem feeder, int calories) =>
        feeder.ModifyTag(StoredCatFoodTag, tag => tag.SetInt(calories).SetEnabled(calories > 0));

    [HarmonyPatch(typeof(MachineFeedDispenser), nameof(MachineFeedDispenser.TryBlendItem))]
    private static class BlendCatFood
    {
        private static void Prefix(GameItem __0, GameItem __1, out (int Other, int CatFood) __state)
        {
            __state = FoodTag != null && __1.IsTag(FoodTag)
                ? (GetOtherCalories(__0), __1.GetTagReadonly(FoodItemHelper.CALORIE_VALUE_TAG).GetInt())
                : default;
        }

        private static void Postfix(GameItem __0, bool __result, (int Other, int CatFood) __state)
        {
            if (!__result || __state.CatFood <= 0)
            {
                return;
            }

            int added = Math.Clamp(GetOtherCalories(__0) - __state.Other, 0, __state.CatFood);
            if (added > 0)
            {
                int previous = Math.Clamp(__0.GetTagReadonly(StoredCatFoodTag).GetInt(), 0, __state.Other);
                SetTrackedCalories(__0, previous + added);
            }
        }
    }

    // All of the public draw overloads end here, including the native animal-feeding calls.
    [HarmonyPatch]
    private static class DrawCatFood
    {
        private static MethodBase TargetMethod()
        {
            Type outCalories = typeof(int).MakeByRefType();
            return AccessTools.Method(typeof(MachineFeedDispenser), nameof(MachineFeedDispenser.DrawKcal),
                new[] { typeof(GameItem), typeof(int), typeof(bool), typeof(bool), typeof(bool),
                    outCalories, outCalories, outCalories });
        }

        private static void Prefix(GameItem __0, out (int Other, int CatFood) __state)
        {
            __state = __0.IsTag(StoredCatFoodTag)
                ? (GetOtherCalories(__0), GetTrackedCalories(__0))
                : default;
        }

        private static void Postfix(GameItem __0, (int Other, int CatFood) __state)
        {
            if (__state.CatFood <= 0)
            {
                return;
            }

            int otherRemaining = GetOtherCalories(__0);
            int drawn = __state.Other - otherRemaining;
            if (drawn > 0)
            {
                int catFoodDrawn = (int)(((long)__state.CatFood * drawn + __state.Other / 2) / __state.Other);
                SetTrackedCalories(__0, Math.Clamp(__state.CatFood - catFoodDrawn, 0, otherRemaining));
            }
        }
    }

    [HarmonyPatch(typeof(MachineFeedDispenser), nameof(MachineFeedDispenser.EmptyStorage))]
    private static class EmptyCatFood
    {
        private static void Postfix(GameItem __0)
        {
            if (__0.IsTag(StoredCatFoodTag))
            {
                SetTrackedCalories(__0, 0);
            }
        }
    }
}
