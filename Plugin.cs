using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Physics_Items.ModCompatFixes;
using Physics_Items.NamedMessages;
using Physics_Items.ItemPhysics;
using Physics_Items.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Xml.Linq;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace Physics_Items
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    [BepInProcess("lethal company.exe")]
    [BepInDependency("Spantle.ThrowEverything", BepInDependency.DependencyFlags.SoftDependency)] // Idk how to add hookgen patcher as a dep.
    [BepInDependency("com.potatoepet.AdvancedCompany", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("Jordo.NeedyCats", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        internal static new ManualLogSource Logger;
        internal static Plugin Instance;
        internal bool Initialized = false;
        internal bool ServerHasMod = false;
        internal HashSet<Type> manualSkipList = new HashSet<Type>();
        internal HashSet<Type> moddedSkipList = new HashSet<Type>();
        internal ConfigEntry<bool> useSourceSounds;
        internal ConfigEntry<bool> overrideAllItemPhysics;
        internal ConfigEntry<bool> physicsOnPickup;
        internal ConfigEntry<bool> disablePlayerCollision;
        internal ConfigEntry<bool> overrideAllModdedItemPhysics;
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

            #region "Configs"
            useSourceSounds = Config.Bind("Fun", "Use Source Engine Collision Sounds", false, "Use source rigidbody sounds.");
            overrideAllItemPhysics = Config.Bind("Fun", "Override all Item Physics", false, "ALL Items will have physics, regardless of issues.");
            overrideAllModdedItemPhysics = Config.Bind("Fun", "Override all MODDED Item Physics", false, "ALL Modded Items will have physics, regardless of issues.");
            physicsOnPickup = Config.Bind("Physics Behaviour", "Physics On Pickup", false, "Only enable item physisc when it has been picked up at least once.");
            disablePlayerCollision = Config.Bind("Physics Behaviour", "Disable Player Collision", false, "Set if Physical Items can collide with players.");
            #endregion

            #region "Harmony Patches"
            Harmony.PatchAll(typeof(ModCheck));
            Harmony.PatchAll(typeof(OnCollision));
            if(ThrowEverythingCompatibility.enabled)
            {
                ThrowEverythingCompatibility.ApplyFixes();
            }
            if(AdvancedCompanyCompatibility.enabled)
            {
                AdvancedCompanyCompatibility.ApplyFixes();
            }
            if (NeedyCatsCompatibility.enabled)
            {
                NeedyCatsCompatibility.ApplyFixes();
            }
            manualSkipList.Add(typeof(ExtensionLadderItem));
            manualSkipList.Add(typeof(RadarBoosterItem));
            #endregion

            #region "MonoMod Hooks"

            ItemPhysics.Environment.Landmine.Init();

            On.GrabbableObject.Start += GrabbableObject_Start;
            On.GrabbableObject.Update += GrabbableObject_Update;
            On.GrabbableObject.EquipItem += GrabbableObject_EquipItem;
            On.GrabbableObject.EnablePhysics += GrabbableObject_EnablePhysics;
            On.GrabbableObject.OnPlaceObject += GrabbableObject_OnPlaceObject;
            On.GrabbableObject.ItemActivate += GrabbableObject_ItemActivate;
            On.GrabbableObject.GrabItem += GrabbableObject_GrabItem;
            On.GameNetcodeStuff.PlayerControllerB.PlaceGrabbableObject += PlayerControllerB_PlaceGrabbableObject;
            On.GameNetcodeStuff.PlayerControllerB.SetObjectAsNoLongerHeld += PlayerControllerB_SetObjectAsNoLongerHeld;
            On.MenuManager.Awake += MenuManager_Awake;
            #endregion
        }

        private void GrabbableObject_GrabItem(On.GrabbableObject.orig_GrabItem orig, GrabbableObject self)
        {
            orig(self);
            Utils.Physics.GetPhysicsComponent(self.gameObject, out PhysicsComponent comp);
            if (comp == null) return;
            comp.alreadyPickedUp = true;
        }

        private void StartOfRound_SyncAlreadyHeldObjectsClientRpc(On.StartOfRound.orig_SyncAlreadyHeldObjectsClientRpc orig, StartOfRound self, NetworkObjectReference[] gObjects, int[] playersHeldBy, int[] itemSlotNumbers, int[] isObjectPocketed, int syncWithClient)
        {
            Logger.LogWarning("CALL!");
            orig(self, gObjects, playersHeldBy, itemSlotNumbers, isObjectPocketed, syncWithClient);
            List<GrabbableObject> grabbableList = FindObjectsByType<GrabbableObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).ToList();
            foreach (GrabbableObject grab in grabbableList)
            {
                grab.transform.parent = self.elevatorTransform;
                //RoundManager.Instance.CollectNewScrapForThisRound(grab);
                GameNetworkManager.Instance.localPlayerController.SetItemInElevator(false, false, grab);
                HUDManager.Instance.AddNewScrapFoundToDisplay(grab);
                //grab.OnBroughtToShip();
            }
        }

        private void PlayerControllerB_PlaceGrabbableObject(On.GameNetcodeStuff.PlayerControllerB.orig_PlaceGrabbableObject orig, GameNetcodeStuff.PlayerControllerB self, Transform parentObject, Vector3 positionOffset, bool matchRotationOfParent, GrabbableObject placeObject)
        {
            orig(self, parentObject, positionOffset, matchRotationOfParent, placeObject);
            if (skipObject.Contains(placeObject)) return;
            Utils.Physics.GetPhysicsComponent(placeObject.gameObject, out PhysicsComponent physics);
            if (physics == null) return;
            physics.isPlaced = true;
            physics.rigidbody.isKinematic = true;
            placeObject.gameObject.transform.rotation = Quaternion.Euler(placeObject.itemProperties.restingRotation.x, placeObject.floorYRot + placeObject.itemProperties.floorYOffset + 90f, placeObject.itemProperties.restingRotation.z);
            placeObject.gameObject.transform.localPosition = positionOffset;
        }

        private void InitializeNetworkTransform()
        {
            if (Initialized) return;
            foreach (GrabbableObject grabbableObject in Resources.FindObjectsOfTypeAll<GrabbableObject>())
            {
                //AddPhysicsComponent(grabbableObject);
                if (skipObject.Contains(grabbableObject) || manualSkipList.Contains(grabbableObject.GetType())) return;
                if (grabbableObject.gameObject.GetComponent<NetworkTransform>() == null) // This is so jank lmfao
                {
                    NetworkTransform netTransform = grabbableObject.gameObject.AddComponent<NetworkTransform>();
                    netTransform.enabled = false;
                }
                    
            }
            Initialized = true;
        }

        // TODO: Optimize code
        private PhysicsComponent AddPhysicsComponent(GrabbableObject grabbableObject)
        {
            if (grabbableObject.gameObject.GetComponent<NetworkObject>() == null) return null;
            if (grabbableObject.gameObject.GetComponent<Rigidbody>() != null || grabbableObject.gameObject.GetComponentInChildren<Rigidbody>() != null)
            {
                Logger.LogWarning($"Skipping Item with Rigidbody: {grabbableObject.gameObject}");
                grabbableObject.gameObject.AddComponent<DestroyHelper>();
                skipObject.Add(grabbableObject);
                return null;
            }else if (manualSkipList.Contains(grabbableObject.GetType()))
            {
                if (overrideAllItemPhysics.Value) return null;
                Logger.LogWarning($"Skipping Vanilla Item: {grabbableObject.gameObject}");
                grabbableObject.gameObject.AddComponent<DestroyHelper>();
                skipObject.Add(grabbableObject);
                return null;
            }else if (moddedSkipList.Contains(grabbableObject.GetType()))
            {
                if (overrideAllModdedItemPhysics.Value) return null;
                Logger.LogWarning($"Skipping Modded Item: {grabbableObject.gameObject}");
                grabbableObject.gameObject.AddComponent<DestroyHelper>();
                skipObject.Add(grabbableObject);
                return null;
            }
            PhysicsComponent component;
            if (!grabbableObject.gameObject.TryGetComponent(out component))
            {
                component = grabbableObject.gameObject.AddComponent<PhysicsComponent>();
            }
            if (component == null)
            {
                Logger.LogError($"Physics Component of {grabbableObject.gameObject} is null! This shouldn't happen!");
                return null;
            }
            Logger.LogInfo($"Successfully added Physics Component to {grabbableObject.gameObject}.");
            if (grabbableObject.TryGetComponent(out Collider collider))
            {
                collider.isTrigger = false; // I'm not sure if this will break anything. I'm doing this because the Teeth item spawns out of existence if isTrigger is true.
            }
            return component;
        }

        #region "MonoMod Patches"
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

        private void GrabbableObject_ItemActivate(On.GrabbableObject.orig_ItemActivate orig, GrabbableObject self, bool used, bool buttonDown)
        {
            // TODO: Don't let players interact with certain objects if the ship is landing
            orig(self, used, buttonDown);
        }

        private void GrabbableObject_OnPlaceObject(On.GrabbableObject.orig_OnPlaceObject orig, GrabbableObject self)
        {
            orig(self);
            if (skipObject.Contains(self)) return;
            Utils.Physics.GetPhysicsComponent(self.gameObject, out PhysicsComponent comp);
            if (comp == null) return;
            comp.EnableColliders(true);
            comp.isPlaced = true;
            comp.rigidbody.isKinematic = true;
        }
        private void MenuManager_Awake(On.MenuManager.orig_Awake orig, MenuManager self)
        {
            orig(self);
            InitializeNetworkTransform();
        }

        private void GrabbableObject_EnablePhysics(On.GrabbableObject.orig_EnablePhysics orig, GrabbableObject self, bool enable)
        {
            if (skipObject.Contains(self))
            {
                orig(self, enable);
            }
            if (Utils.Physics.GetPhysicsComponent(self.gameObject, out PhysicsComponent component))
            {
                component.EnableColliders(enable);
                component.rigidbody.isKinematic = !enable;
                if (enable)
                {
                    component.isPlaced = false;
                }
            }
        }

        private void GrabbableObject_EquipItem(On.GrabbableObject.orig_EquipItem orig, GrabbableObject self)
        {
            orig(self);
            if (skipObject.Contains(self)) return;
            self.transform.parent = null;
            self.EnablePhysics(false);
        }

        private void GrabbableObject_Start(On.GrabbableObject.orig_Start orig, GrabbableObject self)
        {
            if (Utils.Physics.GetPhysicsComponent(self.gameObject) != null) return;
            if (physicsOnPickup.Value)
            {
                skipObject.Add(self);
            }
            PhysicsComponent comp = AddPhysicsComponent(self);
            if (comp != null)
            {
                comp.enabled = false;
                comp.SetPosition();
            }
            orig(self);
        }

        private void GrabbableObject_Update(On.GrabbableObject.orig_Update orig, GrabbableObject self)
        {
            if (skipObject.Contains(self))
            {
                orig(self);
                return;
            }
            if (self == null) return;
            self.fallTime = 1.0f;
            self.reachedFloorTarget = true;
            var wasHeld = self.isHeld;
            self.isHeld = true;
            orig(self);
            self.isHeld = wasHeld;
        }
        #endregion
    }
}
