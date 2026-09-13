using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using JetBrains.Annotations;
using System.Reflection;
using UnityEngine;
using static Heightmap;
using static Unity.IO.LowLevel.Unsafe.AsyncReadManagerMetrics;

namespace ValheimMod
{
    [BepInPlugin(pluginGUID, pluginName, pluginVersion)]
    public class ValheimMod : BaseUnityPlugin
    {
        const string pluginGUID = "se.gusten.ValheimMod";
        const string pluginName = "ValheimMod";
        const string pluginVersion = "1.0.0";

        private Harmony _harmony;

        public static ManualLogSource logger = BepInEx.Logging.Logger.CreateLogSource(pluginName);

        public void Awake()
        {
            ValheimMod.logger.LogInfo("Initializing Gustens ValheimMod");

            _harmony = Harmony.CreateAndPatchAll(typeof(ValheimMod), pluginGUID);
        }

        public void OnDestroy()
        {
            ValheimMod.logger.LogInfo("Unpatching Gustens ValheimMod");

            _harmony?.UnpatchSelf();
        }

        [HarmonyPatch(typeof(Character), "Jump")]
        [HarmonyPrefix]
        public static void Jump_Prefix(ref Character __instance, ref float __state, [HarmonyArgument(0)] bool force)
        {
            // Save the normal jump stamina cost and restore it in PostFix
            __state = __instance.m_jumpStaminaUsage;
            __instance.m_jumpStaminaUsage = 0f;
        }

        [HarmonyPatch(typeof(Character), "Jump")]
        [HarmonyPostfix]
        public static void Jump_Postfix(ref Character __instance, float __state, [HarmonyArgument(0)] bool force)
        {
            // Restore the normal value from Prefix
            __instance.m_jumpStaminaUsage = __state;
        }

        [HarmonyPatch(typeof(Fireplace))]
        [HarmonyPatch("UpdateFireplace")]
        [HarmonyPrefix]
        public static void Fireplace_UpdateFireplace(ref Fireplace __instance)
        {
            // If we're the net owner we set the fuel to max for all fireplaces (does not include smelters and such)
            ZNetView m_nview = (ZNetView)AccessTools.Field(typeof(Fireplace), "m_nview").GetValue(__instance);
            if (!m_nview.IsValid() || !m_nview.IsOwner())
            {
                return;
            }
            m_nview.GetZDO().Set(ZDOVars.s_fuel, __instance.m_maxFuel);
        }

        [HarmonyPatch(typeof(WearNTear), "UpdateWear")]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        public static void WearNTear_UpdateWear(
            ref WearNTear __instance,
            ref ZNetView ___m_nview,
            ref float ___m_rainTimer)
        {
            if (!___m_nview.IsValid() || !___m_nview.IsOwner())
            {
                return;
            }

            ___m_rainTimer = 0f;
        }
    }
}