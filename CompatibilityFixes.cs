using HarmonyLib;
using ThrowEverything.Models;
using UnityEngine;

namespace Physics_Items
{
    internal class CompatibilityFixes
    {
        static Collider colliderCache;
        [HarmonyPatch(typeof(ChargingThrow), nameof(ChargingThrow.DrawLandingCircle)), HarmonyPostfix]
        static void DrawLandingCircle(ChargingThrow __instance)
        {
            if (colliderCache == null)
                colliderCache = __instance.preview.GetComponent<Collider>();
            colliderCache.isTrigger = true; // This is running in PlayerControllerB.Update(). GetComponent bad1!1!1
        }

        [HarmonyPatch(typeof(ChargingThrow), nameof(ChargingThrow.Stop)), HarmonyPostfix]
        static void Stop()
        {
            colliderCache = null;
        }
    }
}
