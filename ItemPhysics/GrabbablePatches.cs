using Physics_Items.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
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

        private static void GrabbableObject_EnablePhysics(On.GrabbableObject.orig_EnablePhysics orig, GrabbableObject self, bool enable)
        {
            if (Plugin.Instance.skipObject.Contains(self))
            {
                orig(self, enable);
                return;
            }
            if (Utils.PhysicsUtil.GetPhysicsComponent(self.gameObject, out PhysicsComponent component))
            {
                component.EnableColliders(enable);
                component.rigidbody.isKinematic = !enable;
                if (enable)
                {
                    component.local_PhysicsVariable.isPlaced = false;
                    if(component.IsOwner) component.net_PhysicsVariable.Value = component.local_PhysicsVariable;
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
            /*if (Utils.Physics.GetPhysicsComponent(self.gameObject) != null)
            {
                orig(self);
                return;
            }
            PhysicsComponent comp = AddPhysicsComponent(self);
            if (Plugin.Instance.physicsOnPickup.Value && comp != null)
            {
                comp.enabled = false;
                Plugin.Instance.skipObject.Add(self);
            }*/
            orig(self);
        }

        private static void GrabbableObject_GrabItem(On.GrabbableObject.orig_GrabItem orig, GrabbableObject self)
        {
            orig(self);
            if (!PhysicsUtil.GetPhysicsComponent(self.gameObject, out PhysicsComponent comp)) return;
            comp.alreadyPickedUp = true;
            //var newSet = new HashSet<Collider>(comp.currentContacts);
            comp.RemoveContact(comp.collider, true);
            comp.currentContacts.Clear();
            /*if (comp.isPushed)
            {
                comp.IsPushed(false);
                //GameNetworkManager.Instance.localPlayerController.carryWeight -= Mathf.Clamp(comp.grabbableObjectRef.itemProperties.weight-1f,0f,10f);
            }*/
            /*var keysToRemove = new List<GameObject>(comp.collisions.Keys);
            // TODO: Might have to sync this with RPCs, and place it in a different method. eg: inside physics component
            foreach (var key in keysToRemove)
            {
                if (key.Equals(comp.gameObject)) continue;
                Plugin.Logger.LogWarning("removing from grab");
                PhysicsUtil.GetPhysicsComponent(key)!.RemoveCollision(comp.gameObject);
                comp.collisions.Remove(key);
            }*/
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
            Utils.PhysicsUtil.GetPhysicsComponent(self.gameObject, out PhysicsComponent comp);
            if (comp == null) return;
            comp.EnableColliders(true);
            comp.local_PhysicsVariable.isPlaced = true;
            if(comp.IsOwner) comp.net_PhysicsVariable.Value = comp.local_PhysicsVariable;
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
