using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using HarmonyLib;
using Physics_Items.NamedMessages;
using Physics_Items.Physics;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.Assertions.Must;

namespace Physics_Items
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    [BepInProcess("lethal company.exe")]
    [BepInDependency("Spantle.ThrowEverything", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        internal static new ManualLogSource Logger;
        internal static Plugin Instance;
        internal bool Initialized = false;
        internal bool ServerHasMod = false;
        private readonly Harmony Harmony = new(PluginInfo.PLUGIN_GUID);

        internal HashSet<GrabbableObject> skipObject = new HashSet<GrabbableObject>();
        private void Awake()
        {
            if (Instance == null)
                Instance = this;
            Logger = base.Logger; // So other files can access Plugin.Instance.Logger.
            // Plugin startup logic
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded! PHYSICS");

            #region "Harmony Patches"
            Harmony.PatchAll(typeof(ModCheck));
            Harmony.PatchAll(typeof(OnCollision));
            if(Chainloader.PluginInfos.TryGetValue("Spantle.ThrowEverything", out BepInEx.PluginInfo info))
            {
                Harmony.PatchAll(typeof(CompatibilityFixes));
            }
            #endregion

            #region "MonoMod Hooks"
            On.GrabbableObject.Start += GrabbableObject_Start;
            On.GrabbableObject.Update += GrabbableObject_Update;
            On.GrabbableObject.EquipItem += GrabbableObject_EquipItem;
            On.GrabbableObject.EnablePhysics += GrabbableObject_EnablePhysics;
            On.GrabbableObject.OnPlaceObject += GrabbableObject_OnPlaceObject;
            On.GrabbableObject.ItemActivate += GrabbableObject_ItemActivate;
            On.GameNetcodeStuff.PlayerControllerB.SetObjectAsNoLongerHeld += PlayerControllerB_SetObjectAsNoLongerHeld;
            On.MenuManager.Awake += MenuManager_Awake;
            //On.StartOfRound.LoadShipGrabbableItems += StartOfRound_LoadShipGrabbableItems;
            #endregion
        }

        private void InitializeNetworkTransform()
        {
            if (Initialized) return;
            foreach (GrabbableObject grabbableObject in Resources.FindObjectsOfTypeAll<GrabbableObject>())
            {
                //AddPhysicsComponent(grabbableObject);
                if(grabbableObject.gameObject.GetComponent<NetworkTransform>() == null) // This is so jank lmfao
                {
                    NetworkTransform netTransform = grabbableObject.gameObject.AddComponent<NetworkTransform>();
                    netTransform.enabled = false;
                }
                    
            }
            Initialized = true;
        }

        private void AddPhysicsComponent(GrabbableObject grabbableObject)
        {
            if (grabbableObject.gameObject.GetComponent<NetworkObject>() == null) return;
            if (grabbableObject.gameObject.GetComponent<Rigidbody>() != null && !skipObject.Contains(grabbableObject))
            {
                Logger.LogWarning($"Item: {grabbableObject.gameObject} already has a rigidbody, skipping.");
                grabbableObject.gameObject.AddComponent<DestroyHelper>();
                skipObject.Add(grabbableObject);
                return;
            }
            PhysicsComponent component;
            if (!grabbableObject.gameObject.TryGetComponent(out component))
            {
                component = grabbableObject.gameObject.AddComponent<PhysicsComponent>();
            }
            if (component == null)
            {
                Logger.LogError($"Physics Component of {grabbableObject.gameObject} is null! This shouldn't happen!");
                return;
            }
            Logger.LogInfo($"Successfully added Physics Component to {grabbableObject.gameObject}.");
            if (grabbableObject.TryGetComponent(out Collider collider))
            {
                collider.isTrigger = false; // I'm not sure if this will break anything.
            }
        }

        #region "MonoMod Patches"
        private void PlayerControllerB_SetObjectAsNoLongerHeld(On.GameNetcodeStuff.PlayerControllerB.orig_SetObjectAsNoLongerHeld orig, GameNetcodeStuff.PlayerControllerB self, bool droppedInElevator, bool droppedInShipRoom, Vector3 targetFloorPosition, GrabbableObject dropObject, int floorYRot)
        {
            orig(self, droppedInElevator, droppedInElevator, targetFloorPosition, dropObject, floorYRot);
            Utils.Physics.GetPhysicsComponent(dropObject.gameObject, out PhysicsComponent comp);
            if (comp == null) return;
            float Distance = (dropObject.startFallingPosition - dropObject.targetFloorPosition).magnitude;
            var direction = (dropObject.targetFloorPosition - dropObject.startFallingPosition).normalized;
            Logger.LogWarning($"Normalized: {direction}");
            bool Thrown = direction + Vector3.up != Vector3.zero;
            var throwForce = Thrown ? Mathf.Min(comp.throwForce, 36f) : 1f;
            var mult = Mathf.Min(Distance * throwForce, comp.rigidbody.mass*10);
            var force = direction * mult;
            //force = new Vector3(force.x, force.y, force.z);
            Logger.LogWarning($"Throwing with force: {force}, {Thrown}, {throwForce}, {comp.throwForce}, {comp.rigidbody.mass}");
            comp.rigidbody.AddForce(force, ForceMode.Impulse);
        }

        private void GrabbableObject_ItemActivate(On.GrabbableObject.orig_ItemActivate orig, GrabbableObject self, bool used, bool buttonDown)
        {
            // TODO: Don't let players interact with certain objects if the ship is landing
            orig(self, used, buttonDown);
        }
        /*private void StartOfRound_LoadShipGrabbableItems(On.StartOfRound.orig_LoadShipGrabbableItems orig, StartOfRound self)
        {
            orig(self);
            List<GrabbableObject> grabbableList = FindObjectsByType<GrabbableObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).ToList();
            foreach (GrabbableObject grab in grabbableList)
            {
                grab.transform.parent = self.elevatorTransform;
                grab.isInShipRoom = true;
                grab.isInElevator = true;
            }
        }*/

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
            AddPhysicsComponent(self);
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
