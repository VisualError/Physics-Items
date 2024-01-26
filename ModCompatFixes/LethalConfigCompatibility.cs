﻿using BepInEx.Configuration;
using HarmonyLib;
using LethalConfig.AutoConfig;
using LethalConfig.ConfigItems;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using ThrowEverything.Models;
using UnityEngine;

namespace Physics_Items.ModCompatFixes
{
    internal static class LethalConfigCompatibility
    {
        private static bool? _enabled;
        private static string modGUID = "ainavt.lc.lethalconfig";
        public static bool enabled
        {
            get
            {
                if (_enabled == null)
                {
                    _enabled = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(modGUID);
                }
                return (bool)_enabled;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public static void ApplyFixes()
        {
            Plugin.Logger.LogInfo($"Applying compatibility fixes to: {modGUID}");
            Plugin.Instance.Harmony.PatchAll(typeof(LethalConfigCompatibility));
        }

        [HarmonyPatch(typeof(AutoConfigGenerator), nameof(AutoConfigGenerator.AutoGenerateConfigs)), HarmonyPostfix]
        public static void AutoGenerateConfigs(ref AutoConfigGenerator.AutoConfigItem[] __result)
        {
            foreach(var configBase in Plugin.Instance.customBlockList)
            {
                BaseConfigItem configItem = AutoConfigGenerator.GenerateConfigForEntry(configBase.Value);
                configItem.IsAutoGenerated = false;
                List<AutoConfigGenerator.AutoConfigItem> list = __result.ToList();
                list.Add(new AutoConfigGenerator.AutoConfigItem
                {
                    ConfigItem = configItem,
                    Assembly = Plugin.Instance.myAssembly
                });
                __result = list.ToArray();
            }
        }
    }
}
