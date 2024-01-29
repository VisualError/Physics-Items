using Physics_Items.Utils;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Physics_Items.ItemPhysics.Environment
{
    internal class PlayerController
    {
        public static void Init()
        {
            On.GameNetcodeStuff.PlayerControllerB.PlaceGrabbableObject += PlayerControllerB_PlaceGrabbableObject;
            On.GameNetcodeStuff.PlayerControllerB.SetObjectAsNoLongerHeld += PlayerControllerB_SetObjectAsNoLongerHeld;
            On.GameNetcodeStuff.PlayerControllerB.DropAllHeldItems += PlayerControllerB_DropAllHeldItems;
        }

        private static void PlayerControllerB_DropAllHeldItems(On.GameNetcodeStuff.PlayerControllerB.orig_DropAllHeldItems orig, GameNetcodeStuff.PlayerControllerB self, bool itemsFall, bool disconnecting)
        {
            var oldItems = new List<GrabbableObject>(self.ItemSlots);
            orig(self, itemsFall, disconnecting);
            if (Utils.PhysicsUtil.GetPhysicsComponent(self.gameObject) == null) return;
            for (int i = 0; i < oldItems.Count; i++)
            {
                GrabbableObject item = oldItems[i];
                if (item is null) continue;
                if (Utils.PhysicsUtil.GetPhysicsComponent(item.gameObject, out PhysicsComponent comp))
                {
                    Plugin.Instance.skipObject.Add(item);
                    comp.physicsHelperRef.why = true;
                    comp.alreadyPickedUp = false;
                    comp.enabled = false;
                }
            }
        }

        private static void PlayerControllerB_PlaceGrabbableObject(On.GameNetcodeStuff.PlayerControllerB.orig_PlaceGrabbableObject orig, GameNetcodeStuff.PlayerControllerB self, Transform parentObject, Vector3 positionOffset, bool matchRotationOfParent, GrabbableObject placeObject)
        {
            orig(self, parentObject, positionOffset, matchRotationOfParent, placeObject);
            Plugin.Logger.LogWarning("placing object");
            if (Plugin.Instance.skipObject.Contains(placeObject)) return;
            Utils.PhysicsUtil.GetPhysicsComponent(placeObject.gameObject, out PhysicsComponent physics);
            if (physics == null) return;
            physics.isPlaced = true;
            physics.rigidbody.isKinematic = true;
            placeObject.gameObject.transform.rotation = Quaternion.Euler(placeObject.itemProperties.restingRotation.x, placeObject.floorYRot + placeObject.itemProperties.floorYOffset + 90f, placeObject.itemProperties.restingRotation.z);
            placeObject.gameObject.transform.localPosition = positionOffset;
        }

        private static void PlayerControllerB_SetObjectAsNoLongerHeld(On.GameNetcodeStuff.PlayerControllerB.orig_SetObjectAsNoLongerHeld orig, GameNetcodeStuff.PlayerControllerB self, bool droppedInElevator, bool droppedInShipRoom, Vector3 targetFloorPosition, GrabbableObject dropObject, int floorYRot)
        {
            orig(self, droppedInElevator, droppedInElevator, targetFloorPosition, dropObject, floorYRot);
            if (Plugin.Instance.skipObject.Contains(dropObject)) return;
            PhysicsUtil.GetPhysicsComponent(dropObject.gameObject, out PhysicsComponent comp);
            if (comp == null) return;
            Plugin.Logger.LogWarning(dropObject.targetFloorPosition);
            Plugin.Logger.LogWarning(targetFloorPosition);
            Plugin.Logger.LogWarning(dropObject.startFallingPosition);
            Vector3 startPosition = new Vector3(dropObject.startFallingPosition.x, 0, dropObject.startFallingPosition.z);
            Vector3 targetPosition = new Vector3(dropObject.targetFloorPosition.x, 0, dropObject.targetFloorPosition.z);

            float distance = PhysicsUtil.CalculateDistance(startPosition, targetPosition);
            Vector3 direction = PhysicsUtil.CalculateDirection(targetPosition, startPosition);
            if (ConfigUtil.DebuggingStuff.Value) Plugin.Logger.LogWarning($"Normalized: {direction}, {distance}");
            bool isThrown = PhysicsUtil.IsThrown(distance);
            float throwForce = PhysicsUtil.CalculateThrowForce(isThrown, comp.throwForce);
            float forceMultiplier = PhysicsUtil.CalculateForceMultiplier(isThrown, distance, throwForce, comp.rigidbody.mass);
            Vector3 force = PhysicsUtil.CalculateForce(forceMultiplier, direction, comp, isThrown);

            PhysicsUtil.ApplyForce(comp, force);
        }
    }
}
