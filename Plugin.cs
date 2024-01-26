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
using System.IO;
using System.Reflection;

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
        internal static Plugin Instance;
        internal bool Initialized = false;
        internal bool ServerHasMod = false;
        internal HashSet<Type> manualSkipList = new HashSet<Type>();
        internal HashSet<Type> blockList = new HashSet<Type>();
        string configDirectory = Paths.ConfigPath;
        internal ConfigEntry<bool> useSourceSounds;
        internal ConfigEntry<bool> physicsOnPickup;
        internal ConfigEntry<bool> disablePlayerCollision;
        internal ConfigEntry<float> maxCollisionVolume;
        internal ConfigEntry<bool> overrideAllItemPhysics;
        internal ConfigEntry<bool> InitializeConfigs;
        internal ConfigEntry<bool> DebuggingStuff;
        internal ConfigFile customBlockList;
        internal Dictionary<string, GrabbableObject> allItemsDictionary = new Dictionary<string, GrabbableObject>();
        internal Assembly myAssembly;
        internal readonly Harmony Harmony = new(PluginInfo.PLUGIN_GUID);

        internal HashSet<GrabbableObject> skipObject = new HashSet<GrabbableObject>();
        private void Awake()
        {
            if (Instance == null)
                Instance = this;
            Logger = base.Logger; // So other files can access Plugin.Instance.Logger.
            // Plugin startup logic
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
            
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
            Harmony.PatchAll(typeof(OnCollision));
            myAssembly = Assembly.GetExecutingAssembly();
            manualSkipList.Add(typeof(ExtensionLadderItem));
            manualSkipList.Add(typeof(RadarBoosterItem));
            #endregion

            #region "Configs"
            InitializeConfigs = Config.Bind("Technical", "Initialize Configs", true, "Re-Initializes all configs when set to true");
            customBlockList = new ConfigFile(Path.Combine(configDirectory, "physicsItems_CustomBlockList.cfg"), true);
            useSourceSounds = Config.Bind("Fun", "Use Source Engine Collision Sounds", false, "Use source rigidbody sounds.");
            overrideAllItemPhysics = Config.Bind("Fun", "Override all Item Physics", false, "ALL Items will have physics, regardless of blocklist.");
            physicsOnPickup = Config.Bind("Physics Behaviour", "Physics On Pickup", false, "Only enable item physisc when it has been picked up at least once.");
            disablePlayerCollision = Config.Bind("Physics Behaviour", "Disable Player Collision", false, "Set if Physical Items can collide with players.");
            maxCollisionVolume = Config.Bind("Physics Behaviour", "Max Collision Volume", 4f, "Sets the max volume each collision should have.");
            DebuggingStuff = Config.Bind("Technical", "Debug", false, "Debug mode");
            customBlockList.SettingChanged += CustomBlockList_SettingChanged;
            Config.SettingChanged += Config_SettingChanged;
            if (InitializeConfigs.Value)
            {
                // Delete the existing config file
                if (File.Exists(Config.ConfigFilePath))
                {
                    File.Delete(Config.ConfigFilePath);
                }
                // Create a new config file with default values
                Config.Save();

                Logger.Log(BepInEx.Logging.LogLevel.All, "Initializing Configs..");
            }
            #endregion

            #region "MonoMod Hooks"

            ItemPhysics.Environment.Landmine.Init();
            GrabbablePatches.Init();
            On.GameNetcodeStuff.PlayerControllerB.PlaceGrabbableObject += PlayerControllerB_PlaceGrabbableObject;
            On.GameNetcodeStuff.PlayerControllerB.SetObjectAsNoLongerHeld += PlayerControllerB_SetObjectAsNoLongerHeld;
            On.GameNetcodeStuff.PlayerControllerB.DropAllHeldItems += PlayerControllerB_DropAllHeldItems;
            On.MenuManager.Awake += MenuManager_Awake;
            #endregion
        }

        private void Config_SettingChanged(object sender, SettingChangedEventArgs e)
        {
            Logger.LogWarning($"Changed: {e.ChangedSetting.Definition.Key} to {e.ChangedSetting.GetSerializedValue()}");
        }

        private void CustomBlockList_SettingChanged(object sender, SettingChangedEventArgs e)
        {
            if (overrideAllItemPhysics.Value) return;
            Logger.LogWarning($"Changed Blocklist: {e.ChangedSetting.Definition.Section} to {e.ChangedSetting.GetSerializedValue()}");
            var grabbable = allItemsDictionary[e.ChangedSetting.Definition.Section];
            List<GrabbableObject> grabbableList = FindObjectsByType<GrabbableObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).ToList();
            if (e.ChangedSetting.GetSerializedValue() == "true")
            {
                foreach(var grab in grabbableList)
                {
                    if(grab.GetType() == grabbable.GetType())
                    {
                        skipObject.Add(grab);
                        blockList.Add(grab.GetType());
                    }
                }
            }
            else
            {
                foreach (var grab in grabbableList)
                {
                    if (grab.GetType() == grabbable.GetType())
                    {
                        skipObject.Remove(grab);
                        blockList.Remove(grab.GetType());
                    }
                }
            }
        }

        private void InitializeNetworkTransformAndBlocklistConfig()
        {
            if (Initialized) return;
            foreach (GrabbableObject grabbableObject in Resources.FindObjectsOfTypeAll<GrabbableObject>())
            {
                InitializeBlocklistConfig(grabbableObject);
                if (manualSkipList.Contains(grabbableObject.GetType())) continue;
                if (grabbableObject.gameObject.GetComponent<NetworkTransform>() == null) // This is so jank lmfao
                {
                    NetworkTransform netTransform = grabbableObject.gameObject.AddComponent<NetworkTransform>();
                    netTransform.enabled = false;
                }
            }
            Initialized = true;
            InitializeConfigs.Value = false;
        }

        private void InitializeBlocklistConfig(GrabbableObject grabbableObject)
        {
            var value = false;
            if(grabbableObject == null)
            {
                return;
            }
            if (grabbableObject.itemProperties == null)
            {
                Logger.LogWarning("Skipping item with no item properties");
                return;
            }
            if (grabbableObject.itemProperties.itemName.IsNullOrWhiteSpace())
            {
                Logger.LogWarning("Skipping item with no item name");
                return;
            }
            if (manualSkipList.Contains(grabbableObject.GetType()) || grabbableObject.GetComponent<Rigidbody>() != null) value = true;
            allItemsDictionary[grabbableObject.itemProperties.itemName] = grabbableObject;
            ConfigDefinition configDef = new ConfigDefinition(grabbableObject.itemProperties.itemName, "Add to blocklist");
            customBlockList.Bind(configDef, value, new ConfigDescription("If check/true, adds to blocklist. [REQUIRES RESTART]"));
            if (InitializeConfigs.Value)
            {
                customBlockList[configDef].BoxedValue = value;
            }
            if (customBlockList[configDef].GetSerializedValue() == "true")
            {
                skipObject.Add(grabbableObject);
                blockList.Add(grabbableObject.GetType());
            }
            Logger.LogInfo($"Added: {grabbableObject.itemProperties.itemName} to config (Default: {value}, current: {customBlockList[configDef].GetSerializedValue()})");
        }

        #region "MonoMod Patches"
        private void PlayerControllerB_DropAllHeldItems(On.GameNetcodeStuff.PlayerControllerB.orig_DropAllHeldItems orig, GameNetcodeStuff.PlayerControllerB self, bool itemsFall, bool disconnecting)
        {
            var oldItems = new List<GrabbableObject>(self.ItemSlots);
            orig(self, itemsFall, disconnecting);
            if (Utils.Physics.GetPhysicsComponent(self.gameObject) == null) return;
            for (int i = 0; i < oldItems.Count; i++)
            {
                GrabbableObject item = oldItems[i];
                if (item is null) continue;
                if (Utils.Physics.GetPhysicsComponent(item.gameObject, out PhysicsComponent comp))
                {
                    skipObject.Add(item);
                    comp.physicsHelperRef.why = true;
                    comp.alreadyPickedUp = false;
                    comp.enabled = false;
                }
            }
        }

        private void PlayerControllerB_PlaceGrabbableObject(On.GameNetcodeStuff.PlayerControllerB.orig_PlaceGrabbableObject orig, GameNetcodeStuff.PlayerControllerB self, Transform parentObject, Vector3 positionOffset, bool matchRotationOfParent, GrabbableObject placeObject)
        {
            orig(self, parentObject, positionOffset, matchRotationOfParent, placeObject);
            Logger.LogWarning("placing object");
            if (skipObject.Contains(placeObject)) return;
            Utils.Physics.GetPhysicsComponent(placeObject.gameObject, out PhysicsComponent physics);
            if (physics == null) return;
            physics.isPlaced = true;
            physics.rigidbody.isKinematic = true;
            placeObject.gameObject.transform.rotation = Quaternion.Euler(placeObject.itemProperties.restingRotation.x, placeObject.floorYRot + placeObject.itemProperties.floorYOffset + 90f, placeObject.itemProperties.restingRotation.z);
            placeObject.gameObject.transform.localPosition = positionOffset;
        }

        private void PlayerControllerB_SetObjectAsNoLongerHeld(On.GameNetcodeStuff.PlayerControllerB.orig_SetObjectAsNoLongerHeld orig, GameNetcodeStuff.PlayerControllerB self, bool droppedInElevator, bool droppedInShipRoom, Vector3 targetFloorPosition, GrabbableObject dropObject, int floorYRot)
        {
            orig(self, droppedInElevator, droppedInElevator, targetFloorPosition, dropObject, floorYRot);
            if (skipObject.Contains(dropObject)) return;
            Utils.Physics.GetPhysicsComponent(dropObject.gameObject, out PhysicsComponent comp);
            if (comp == null) return;
            float Distance = (dropObject.startFallingPosition - dropObject.targetFloorPosition).magnitude;
            var direction = (dropObject.targetFloorPosition - dropObject.startFallingPosition).normalized;
            Logger.LogWarning($"Normalized: {direction}");
            bool Thrown = direction + Vector3.up != Vector3.zero;
            var throwForce = Thrown ? Mathf.Min(comp.throwForce, 36f) : 0f;
            var mult = throwForce == 0f ? 0f : Mathf.Min(Distance * throwForce, comp.rigidbody.mass*10);
            var force = mult == 0f ? Vector3.zero : direction * mult;
            //force = new Vector3(force.x, force.y, force.z);
            Logger.LogWarning($"Throwing with force: {force}, {Thrown}, {throwForce}, {comp.throwForce}, {comp.rigidbody.mass}");
            comp.rigidbody.AddForce(force, ForceMode.Impulse);
        }

        private void MenuManager_Awake(On.MenuManager.orig_Awake orig, MenuManager self)
        {
            orig(self);
            InitializeNetworkTransformAndBlocklistConfig();
        }

        #endregion
    }
}
