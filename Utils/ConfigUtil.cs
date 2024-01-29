using BepInEx.Configuration;
using BepInEx;
using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using BepInEx.Logging;
using UnityEngine;
using System.Linq;

namespace Physics_Items.Utils
{
    internal class ConfigUtil
    {
        static string configDirectory = Paths.ConfigPath;
        static internal ConfigEntry<bool> useSourceSounds;
        static internal ConfigEntry<bool> physicsOnPickup;
        static internal ConfigEntry<bool> disablePlayerCollision;
        static internal ConfigEntry<float> maxCollisionVolume;
        static internal ConfigEntry<bool> overrideAllItemPhysics;
        static internal ConfigEntry<bool> InitializeConfigs;
        static internal ConfigEntry<bool> DebuggingStuff;
        static internal ConfigEntry<float> DiscardFollowAmplitude;
        static internal ConfigFile customBlockList;
        public static void Init()
        {
            InitializeConfigs = Plugin.Config.Bind("Technical", "Initialize Configs", true, "Re-Initializes all configs when set to true");
            customBlockList = new ConfigFile(Path.Combine(configDirectory, "physicsItems_CustomBlockList.cfg"), true);
            useSourceSounds = Plugin.Config.Bind("Fun", "Use Source Engine Collision Sounds", false, "Use source rigidbody sounds.");
            overrideAllItemPhysics = Plugin.Config.Bind("Fun", "Override all Item Physics", false, "ALL Items will have physics, regardless of blocklist.");
            physicsOnPickup = Plugin.Config.Bind("Physics Behaviour", "Physics On Pickup", false, "Only enable item physisc when it has been picked up at least once.");
            disablePlayerCollision = Plugin.Config.Bind("Physics Behaviour", "Disable Player Collision", false, "Set if Physical Items can collide with players.");
            maxCollisionVolume = Plugin.Config.Bind("Physics Behaviour", "Max Collision Volume", 4f, "Sets the max volume each collision should have.");
            DebuggingStuff = Plugin.Config.Bind("Technical", "Debug", false, "Debug mode");
            DiscardFollowAmplitude = Plugin.Config.Bind("Physics Behaviour", "Discard Follow Aplitude", 36f, "Sets how strong items should go with the players velocity. In VR this sets how hard you'll be able to throw items.");
            customBlockList.SettingChanged += CustomBlockList_SettingChanged;
            Plugin.Config.SettingChanged += Config_SettingChanged;
            if (InitializeConfigs.Value)
            {
                // Delete the existing config file
                if (File.Exists(Plugin.Config.ConfigFilePath))
                {
                    File.Delete(Plugin.Config.ConfigFilePath);
                }
                // Create a new config file with default values
                Plugin.Config.Save();

                Plugin.Logger.Log(LogLevel.All, "Initializing Configs..");
            }
        }

        public static void InitializeBlocklistConfig(GrabbableObject grabbableObject)
        {
            var value = false;
            string? name;
            if (grabbableObject == null)
            {
                return;
            }
            if (grabbableObject.itemProperties == null)
            {
                Plugin.Logger.LogWarning("Skipping item with no item properties");
                return;
            }
            if (grabbableObject.itemProperties.itemName.IsNullOrWhiteSpace())
            {
                Plugin.Logger.LogWarning("Skipping item with no item name");
                name = grabbableObject.itemProperties.name;
            }
            else
            {
                name = grabbableObject.itemProperties.itemName;
            }
            name = StringUtil.SanitizeString(ref name);
            if (name.IsNullOrWhiteSpace())
            {
                Plugin.Logger.LogWarning("Skipping item with null name.");
                return;
            }
            if (Plugin.Instance.manualSkipList.Contains(grabbableObject.GetType()) || grabbableObject.GetComponent<Rigidbody>() != null) value = true;
            Plugin.Instance.allItemsDictionary[name] = grabbableObject;
            ConfigDefinition configDef = new ConfigDefinition(name, "Add to blocklist");
            customBlockList.Bind(configDef, value, new ConfigDescription("If check/true, adds to blocklist. [REQUIRES RESTART]"));
            if (InitializeConfigs.Value)
            {
                customBlockList[configDef].BoxedValue = value;
            }
            if (customBlockList[configDef].GetSerializedValue().Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                Plugin.Instance.skipObject.Add(grabbableObject);
                Plugin.Instance.blockList.Add(grabbableObject.GetType());
            }
            if(InitializeConfigs.Value) Plugin.Logger.LogInfo($"Added: {name} to blocklist configuration. (Default: {value}, Current: {customBlockList[configDef].GetSerializedValue()})");
        }

        static private void Config_SettingChanged(object sender, SettingChangedEventArgs e)
        {
            Plugin.Logger.LogWarning($"Changed: {e.ChangedSetting.Definition.Key} to {e.ChangedSetting.GetSerializedValue()}");
        }

        static private void CustomBlockList_SettingChanged(object sender, SettingChangedEventArgs e)
        {
            if (overrideAllItemPhysics.Value) return;
            Plugin.Logger.LogWarning($"Changed Blocklist: {e.ChangedSetting.Definition.Section} to {e.ChangedSetting.GetSerializedValue()}");
            var grabbable = Plugin.Instance.allItemsDictionary[e.ChangedSetting.Definition.Section];
            List<GrabbableObject> grabbableList = GameObject.FindObjectsByType<GrabbableObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).ToList();
            if (e.ChangedSetting.GetSerializedValue() == "true")
            {
                foreach (var grab in grabbableList)
                {
                    if (grab.GetType() == grabbable.GetType())
                    {
                        Plugin.Instance.skipObject.Add(grab);
                        Plugin.Instance.blockList.Add(grab.GetType());
                    }
                }
            }
            else
            {
                foreach (var grab in grabbableList)
                {
                    if (grab.GetType() == grabbable.GetType())
                    {
                        Plugin.Instance.skipObject.Remove(grab);
                        Plugin.Instance.blockList.Remove(grab.GetType());
                    }
                }
            }
        }
    }
}
