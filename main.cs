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
        public static bool WearNTear_UpdateWear(ref WearNTear __instance, [HarmonyArgument(0)] float time)
        {
            ZNetView m_nview = (ZNetView)AccessTools.Field(typeof(WearNTear), "m_nview").GetValue(__instance);
            if (!m_nview.IsValid())
            {
                return false;
            }

            bool shouldUpdate = (bool)AccessTools.Method(typeof(WearNTear), "ShouldUpdate").Invoke(__instance, new object[] { time });
            if (m_nview.IsOwner() && shouldUpdate)
            {
                if (ZNetScene.instance.OutsideActiveArea(__instance.transform.position))
                {
                    float maxSupport = (float)AccessTools.Method(typeof(WearNTear), "GetMaxSupport").Invoke(__instance, new object[] { });
                    float m_support = (float)AccessTools.Field(typeof(WearNTear), "m_support").GetValue(__instance);
                    if (!m_support.Equals(maxSupport))
                    {
                        m_nview.GetZDO().Set(ZDOVars.s_support, maxSupport);
                    }

                    return false;
                }

                int m_shieldChangeID = (int)AccessTools.Field(typeof(WearNTear), "m_shieldChangeID").GetValue(__instance);
                bool flag = ShieldGenerator.IsInsideShieldCached(__instance.transform.position, ref m_shieldChangeID);
                float num = 0f;

                bool m_haveRoof = (bool)AccessTools.Field(typeof(WearNTear), "m_haveRoof").GetValue(__instance);
                bool m_noRoofWear = (bool)AccessTools.Field(typeof(WearNTear), "m_noRoofWear").GetValue(__instance);
                bool rainWet = !flag && !m_haveRoof && m_noRoofWear && EnvMan.IsWet();
                AccessTools.Field(typeof(WearNTear), "m_rainWet").SetValue(__instance, rainWet);
                if ((bool)(UnityEngine.Object)__instance.m_wet)
                {
                    __instance.m_wet.SetActive(rainWet);
                }

                if (m_noRoofWear && !flag && __instance.GetHealthPercentage() > 0.5f)
                {
                    AccessTools.Field(typeof(WearNTear), "m_rainTimer").SetValue(__instance, 0f);
                }

                if (__instance.m_noSupportWear)
                {
                    AccessTools.Method(typeof(WearNTear), "UpdateSupport").Invoke(__instance, new object[] { });
                    bool haveSupport = (bool)AccessTools.Method(typeof(WearNTear), "HaveSupport").Invoke(__instance, new object[] { });
                    if (!haveSupport)
                    {
                        num = 100f;
                    }
                }

                if (!string.IsNullOrEmpty(__instance.m_requiredPersistentEvent))
                {
                    PersistentEventSystem.PersistentEvent activeEvent = PersistentEventSystem.instance.GetActiveEvent(__instance.transform.position);
                    if (__instance.m_takeDamageIfInsideEvent && activeEvent != null && activeEvent.internalName == __instance.m_requiredPersistentEvent)
                    {
                        num += __instance.m_eventDamage + UnityEngine.Random.Range(0f - __instance.m_eventDamageDeviation, __instance.m_eventDamageDeviation);
                    }
                    else if ((activeEvent != null && activeEvent.internalName != __instance.m_requiredPersistentEvent) || activeEvent == null)
                    {
                        num += __instance.m_eventDamage + UnityEngine.Random.Range(0f - __instance.m_eventDamageDeviation, __instance.m_eventDamageDeviation);
                    }
                }

                bool flag2 = false;
                AccessTools.Method(typeof(WearNTear), "UpdateBiome").Invoke(__instance, new object[] { });
                Heightmap.Biome m_biome = (Heightmap.Biome)AccessTools.Field(typeof(WearNTear), "m_biome").GetValue(__instance);
                if (m_biome == Heightmap.Biome.DeepNorth)
                {
                    bool flag3 = (bool)AccessTools.Method(typeof(WearNTear), "CanHaveSnow").Invoke(__instance, new object[] { flag });
                    if (flag3 && __instance.m_snowBuildup < 1f)
                    {
                        AccessTools.Field(typeof(WearNTear), "m_heavySnow").SetValue(__instance, false);
                        bool m_addPreSnow = (bool)AccessTools.Field(typeof(WearNTear), "m_addPreSnow").GetValue(__instance);
                        float num2 = (m_addPreSnow ? 1f : EnvMan.instance.GetSnowBuildup());
                        if (num2 > 0f)
                        {
                            if (m_addPreSnow)
                            {
                                __instance.m_snowBuildup = 1f;
                                m_addPreSnow = false;
                                m_nview.GetZDO().Set(ZDOVars.s_preSnow, value: false);
                            }
                            else
                            {
                                __instance.m_snowBuildup += num2 * Time.deltaTime * Game.instance.m_snowBuildupSpeed;
                            }

                            m_nview.GetZDO().Set(ZDOVars.s_snow, __instance.m_snowBuildup);
                        }
                    }
                    else if ((!__instance.m_snowDamageImmune || ZoneSystem.instance.GetGlobalKey(GlobalKeys.AllHeavySnow)) && !ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoHeavySnow))
                    {
                        bool m_heavySnow = flag3;
                        AccessTools.Field(typeof(WearNTear), "m_heavySnow").SetValue(__instance, m_heavySnow);
                        if (m_heavySnow)
                        {
                            float m_lastSupportColorValue = (float)AccessTools.Method(typeof(WearNTear), "GetSupportColorValue").Invoke(__instance, new object[] { });
                            AccessTools.Field(typeof(WearNTear), "m_lastSupportColorValue").SetValue(__instance, m_lastSupportColorValue);
                            if (m_lastSupportColorValue != -1f && m_lastSupportColorValue < Game.instance.m_snowSupportLevel)
                            {
                                AccessTools.Field(typeof(WearNTear), "m_heavySnow").SetValue(__instance, true);
                                num += Game.instance.m_snowDamage;
                                float m_snowDamageTimer = (float)AccessTools.Field(typeof(WearNTear), "m_snowDamageTimer").GetValue(__instance);
                                if (ZNet.instance.GetTimeSeconds() >= (double)m_snowDamageTimer)
                                {
                                    m_snowDamageTimer = (float)ZNet.instance.GetTimeSeconds() + UnityEngine.Random.Range(Game.instance.m_snowDamageEffectIntervalRange.x, Game.instance.m_snowDamageEffectIntervalRange.y);
                                    AccessTools.Field(typeof(WearNTear), "m_snowDamageTimer").SetValue(__instance, m_snowDamageTimer);
                                    Game.instance.m_snowDamageEffect.Create(__instance.transform.position, Quaternion.identity, __instance.transform);
                                }
                            }
                        }
                    }
                }

                if (m_biome == Heightmap.Biome.AshLands)
                {
                    bool m_inAshlands = true;
                    AccessTools.Field(typeof(WearNTear), "m_inAshlands").SetValue(__instance, m_inAshlands);
                    if (Game.instance.m_ashDamage > 0f && !flag && !__instance.m_ashDamageImmune)
                    {
                        bool m_haveAshRoof = (bool)AccessTools.Field(typeof(WearNTear), "m_haveAshRoof").GetValue(__instance);
                        flag2 = !m_haveAshRoof && (!__instance.m_ashDamageResist || __instance.GetHealthPercentage() > 0.1f);
                        if (flag2)
                        {
                            float m_ashTimer = (float)AccessTools.Field(typeof(WearNTear), "m_ashTimer").GetValue(__instance);
                            if (m_ashTimer == 0f)
                            {
                                AccessTools.Field(typeof(WearNTear), "m_ashTimer").SetValue(__instance, time);
                            }
                            else if (time - m_ashTimer > 5f)
                            {
                                AccessTools.Field(typeof(WearNTear), "m_ashTimer").SetValue(__instance, time);
                                num += Game.instance.m_ashDamage;
                            }
                        }
                        else
                        {
                            AccessTools.Field(typeof(WearNTear), "m_ashTimer").SetValue(__instance, 0f);
                        }
                    }

                    float m_lavaValue = (float)AccessTools.Field(typeof(WearNTear), "m_lavaValue").GetValue(__instance);
                    if (!__instance.m_staticPosition)
                    {
                        Heightmap heightmap = (Heightmap)AccessTools.Field(typeof(WearNTear), "m_heightmap").GetValue(__instance);
                        m_lavaValue = heightmap.GetLava(__instance.transform.position);
                        AccessTools.Field(typeof(WearNTear), "m_lavaValue").SetValue(__instance, m_lavaValue);
                    }

                    float m_groundDist = (float)AccessTools.Field(typeof(WearNTear), "m_groundDist").GetValue(__instance);
                    float m_lavaTimer = (float)AccessTools.Field(typeof(WearNTear), "m_lavaTimer").GetValue(__instance);
                    if (m_lavaValue > 0.2f && m_groundDist < 1.5f && !__instance.m_ashDamageImmune)
                    {
                        if (m_lavaTimer == 0f)
                        {
                            AccessTools.Field(typeof(WearNTear), "m_lavaTimer").SetValue(__instance, time);
                        }
                        else if (time - m_lavaTimer > 2f)
                        {
                            AccessTools.Field(typeof(WearNTear), "m_lavaTimer").SetValue(__instance, time);
                            float num3 = (flag ? 30f : 70f) * m_lavaValue;
                            bool m_ashDamageResist = (bool)AccessTools.Field(typeof(WearNTear), "m_ashDamageResist").GetValue(__instance);
                            num += num3 * (m_ashDamageResist ? 0.33f : 1f);
                        }
                    }
                    else
                    {
                        AccessTools.Field(typeof(WearNTear), "m_lavaTimer").SetValue(__instance, 0f);
                    }
                }

                if (__instance.m_requiredBiome != 0 && !__instance.m_requiredBiome.HasFlag(m_biome))
                {
                    num += __instance.m_outsideRequiredBiomeDamage;
                }

                AccessTools.Field(typeof(WearNTear), "m_ashDamageTime").SetValue(__instance, (flag2 ? 5f : 0f));
                bool CanBeRemoved = (bool)AccessTools.Method(typeof(WearNTear), "CanBeRemoved").Invoke(__instance, new object[] { });
                if (num > 0f && !CanBeRemoved)
                {
                    num = 0f;
                }

                if (num > 0f)
                {
                    float damage = num / 100f * __instance.m_health;
                    if (!ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoBuildingFall))
                    {
                        __instance.ApplyDamage(damage);
                    }
                }
            }

            if ((bool)__instance.m_snow && m_nview.IsValid())
            {
                __instance.m_snowBuildup = m_nview.GetZDO().GetFloat(ZDOVars.s_snow);
            }

            AccessTools.Method(typeof(WearNTear), "UpdateVisual").Invoke(__instance, new object[] { true });
            return false;
        }
    }
}