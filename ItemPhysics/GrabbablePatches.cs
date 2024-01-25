using System;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;

namespace Physics_Items.ItemPhysics
{
    internal class GrabbablePatches
    {
        public static void Init()
        {
            On.GrabbableObject.Start += GrabbableObject_Start;
            On.GrabbableObject.Update += GrabbableObject_Update;
            On.GrabbableObject.EquipItem += GrabbableObject_EquipItem;
            On.GrabbableObject.EnablePhysics += GrabbableObject_EnablePhysics;
            On.GrabbableObject.OnPlaceObject += GrabbableObject_OnPlaceObject;
            On.GrabbableObject.ItemActivate += GrabbableObject_ItemActivate;
            On.GrabbableObject.GrabItem += GrabbableObject_GrabItem;
        }

        // TODO: Optimize code
        private static PhysicsComponent AddPhysicsComponent(GrabbableObject grabbableObject)
        {
            if (grabbableObject.gameObject.GetComponent<NetworkObject>() == null) return null;
            if (grabbableObject.gameObject.GetComponent<Rigidbody>() != null || grabbableObject.gameObject.GetComponentInChildren<Rigidbody>() != null)
            {
                Plugin.Logger.LogWarning($"Skipping Item with Rigidbody: {grabbableObject.gameObject}");
                grabbableObject.gameObject.AddComponent<DestroyHelper>();
                Plugin.Instance.skipObject.Add(grabbableObject);
                return null;
            }
            else if (Plugin.Instance.blockList.Contains(grabbableObject.GetType()))
            {
                if (Plugin.Instance.overrideAllModdedItemPhysics.Value) return null;
                Plugin.Logger.LogWarning($"Skipping Blocked Item: {grabbableObject.gameObject}");
                grabbableObject.gameObject.AddComponent<DestroyHelper>();
                Plugin.Instance.skipObject.Add(grabbableObject);
                return null;
            }
            PhysicsComponent component;
            if (!grabbableObject.gameObject.TryGetComponent(out component))
            {
                component = grabbableObject.gameObject.AddComponent<PhysicsComponent>();
            }
            if (component == null)
            {
                Plugin.Logger.LogError($"Physics Component of {grabbableObject.gameObject} is null! This shouldn't happen!");
                return null;
            }
            Plugin.Logger.LogInfo($"Successfully added Physics Component to {grabbableObject.gameObject}.");
            if (grabbableObject.TryGetComponent(out Collider collider))
            {
                collider.isTrigger = false; // I'm not sure if this will break anything. I'm doing this because the Teeth item spawns out of existence if isTrigger is true.
            }
            return component;
        }

        private static void GrabbableObject_EnablePhysics(On.GrabbableObject.orig_EnablePhysics orig, GrabbableObject self, bool enable)
        {
            if (Plugin.Instance.skipObject.Contains(self))
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

        private static void GrabbableObject_EquipItem(On.GrabbableObject.orig_EquipItem orig, GrabbableObject self)
        {
            orig(self);
            if (Plugin.Instance.skipObject.Contains(self)) return;
            self.transform.parent = null;
            self.EnablePhysics(false);
        }

        private static void GrabbableObject_Start(On.GrabbableObject.orig_Start orig, GrabbableObject self)
        {
            if (Utils.Physics.GetPhysicsComponent(self.gameObject) != null) return;
            if (Plugin.Instance.physicsOnPickup.Value)
            {
                Plugin.Instance.skipObject.Add(self);
            }
            PhysicsComponent comp = AddPhysicsComponent(self);
            if (comp != null)
            {
                comp.enabled = false;
                comp.SetPosition();
            }
            orig(self);
        }

        private static void GrabbableObject_GrabItem(On.GrabbableObject.orig_GrabItem orig, GrabbableObject self)
        {
            orig(self);
            Utils.Physics.GetPhysicsComponent(self.gameObject, out PhysicsComponent comp);
            if (comp == null) return;
            comp.alreadyPickedUp = true;
        }

        private static void GrabbableObject_ItemActivate(On.GrabbableObject.orig_ItemActivate orig, GrabbableObject self, bool used, bool buttonDown)
        {
            // TODO: Don't let players interact with certain objects if the ship is landing
            orig(self, used, buttonDown);
        }

        private static void GrabbableObject_OnPlaceObject(On.GrabbableObject.orig_OnPlaceObject orig, GrabbableObject self)
        {
            orig(self);
            if (Plugin.Instance.skipObject.Contains(self)) return;
            Utils.Physics.GetPhysicsComponent(self.gameObject, out PhysicsComponent comp);
            if (comp == null) return;
            comp.EnableColliders(true);
            comp.isPlaced = true;
            comp.rigidbody.isKinematic = true;
        }

        private static void GrabbableObject_Update(On.GrabbableObject.orig_Update orig, GrabbableObject self)
        {
            if (Plugin.Instance.skipObject.Contains(self))
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
    }
}
