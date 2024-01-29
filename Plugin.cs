using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Physics_Items.ModCompatFixes;
using Physics_Items.NamedMessages;
using Physics_Items.ItemPhysics;
using Physics_Items.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode.Components;
using UnityEngine;
using System.Reflection;
using Physics_Items.ItemPhysics.Environment;

namespace Physics_Items
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    [BepInProcess("lethal company.exe")]
    [BepInDependency("Spantle.ThrowEverything", BepInDependency.DependencyFlags.SoftDependency)] // Idk how to add hookgen patcher as a dep.
    [BepInDependency("com.potatoepet.AdvancedCompany", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("Jordo.NeedyCats", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("ainavt.lc.lethalconfig", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.malco.lethalcompany.moreshipupgrades", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        internal static new ManualLogSource Logger;
        internal static new ConfigFile Config;
        internal static Plugin Instance;
        internal bool Initialized = false;
        internal HashSet<Type> manualSkipList = new HashSet<Type>();
        internal HashSet<Type> blockList = new HashSet<Type>();
        internal Dictionary<string, GrabbableObject> allItemsDictionary = new Dictionary<string, GrabbableObject>();
        internal Assembly myAssembly;
        internal readonly Harmony Harmony = new(PluginInfo.PLUGIN_GUID);

        internal HashSet<GrabbableObject> skipObject = new HashSet<GrabbableObject>();

        void NetcodePatch()
        {
            var types = Assembly.GetExecutingAssembly().GetTypes();
            foreach (var type in types)
            {
                var methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                foreach (var method in methods)
                {
                    try
                    {
                        var attributes = method.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false);
                        if (attributes.Length > 0)
                        {
                            method.Invoke(null, null);
                        }
                    }catch(Exception e)
                    {
                        Logger.LogWarning($"Caugh Exception: {e.Message} (Open log file for full log..)");
                        Logger.LogDebug($"Full Exception: {e}");
                    }
                }
            }
        }

        private void Awake()
        {
            if (Instance == null)
                Instance = this;
            Config = base.Config;
            Logger = base.Logger; // So other files can access Plugin.Instance.Logger.
            // Plugin startup logic
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded! Items will now have physics.");
            AssetLoader.LoadAssetBundles();

            #region "Compatibility"
            if (ThrowEverythingCompatibility.enabled)
            {
                ThrowEverythingCompatibility.ApplyFixes();
            }
            if (AdvancedCompanyCompatibility.enabled)
            {
                AdvancedCompanyCompatibility.ApplyFixes();
            }
            if (NeedyCatsCompatibility.enabled)
            {
                NeedyCatsCompatibility.ApplyFixes();
            }
            if (LateGameUpgradesCompatibility.enabled)
            {
                LateGameUpgradesCompatibility.ApplyFixes();
            }
            if (LethalConfigCompatibility.enabled)
            {
                LethalConfigCompatibility.ApplyFixes();
            }
            #endregion

            #region "Harmony Patches"
            Harmony.PatchAll(typeof(ModCheck));
            //Harmony.PatchAll(typeof(ItemSpawnTranspiler));
            myAssembly = Assembly.GetExecutingAssembly();
            manualSkipList.Add(typeof(ExtensionLadderItem));
            manualSkipList.Add(typeof(RadarBoosterItem));
            #endregion

            #region "MonoMod Hooks"

            ItemPhysics.Environment.LandmineController.Init();
            GrabbablePatches.Init();
            PlayerController.Init();
            On.GameNetworkManager.Awake += GameNetworkManager_Awake;
            #endregion

            NetcodePatch();
            ConfigUtil.Init();
        }

        private void GameNetworkManager_Awake(On.GameNetworkManager.orig_Awake orig, GameNetworkManager self)
        {
            orig(self);
            InitializeNetworkTransformAndBlocklistConfig();
        }

        private void InitializeNetworkTransformAndBlocklistConfig()
        {
            if (Initialized) return;
            foreach (GrabbableObject grabbableObject in Resources.FindObjectsOfTypeAll<GrabbableObject>())
            {
                if (grabbableObject.gameObject == null) continue;
                ConfigUtil.InitializeBlocklistConfig(grabbableObject);
                if (blockList.Contains(grabbableObject.GetType()))
                {
                    Logger.LogWarning($"Skipping {grabbableObject}");
                    continue;
                }
                if (grabbableObject.gameObject.GetComponent<NetworkTransform>() == null) // This is so jank lmfao
                {
                    NetworkTransform netTransform = grabbableObject.gameObject.AddComponent<NetworkTransform>();
                    netTransform.enabled = false;
                }
                PhysicsUtil.AddPhysicsComponent(grabbableObject);
            }
            Initialized = true;
            ConfigUtil.InitializeConfigs.Value = false;
        }
    }
}
