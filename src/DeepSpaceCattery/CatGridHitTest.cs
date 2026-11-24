using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DeepSpaceCattery;

public sealed partial class GameMod
{
    [HarmonyPatch]
    private static class CatGridHitTest
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(GameItemElement), "ValidateSelectLocked");
            yield return AccessTools.Method(typeof(GameItemElement), nameof(GameItemElement.ValidatePartialSprite));
        }

        private static void Postfix(GameItemElement __instance)
        {
            if (pedigreeClickOwner is not { enabled: true } mod)
            {
                return;
            }

            string itemId = __instance.identifier;
            if (!mod.IsCatItem(__instance) && itemId != mod.CatFoodId &&
                itemId != mod.HouseId && itemId != mod.PreviousHouseId && itemId != mod.PortableCatteryId &&
                itemId != mod.FurProcessorId)
            {
                return;
            }

            // The native rotator already applies the grid position, rotation and flip.
            // Use the occupied grid rather than the sprite's transparency or PPU.
            var size = new Vector2(__instance.widthPixels, __instance.heightPixels);
            var background = __instance.background.image;
            background.alphaHitTestMinimumThreshold = 0;
            background.rectTransform.sizeDelta = size;
            __instance.fakeBackground.image.rectTransform.sizeDelta = size;
        }
    }
}
