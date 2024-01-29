using Physics_Items.Utils;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Physics_Items.ItemPhysics
{
    // TODO: Possibly turn this into a handler for RPC related shit, make it a network behaviour and somehow register the prefab without fucking up joining servers that doesnt have the mod.
    public class PhysicsHelper : MonoBehaviour
    {
        PhysicsComponent physicsComponentRef;
        GrabbableObject grabbableObjectRef;
        Type cachedType;
        void Awake()
        {
            if(!TryGetComponent(out physicsComponentRef))
            {
                Plugin.Logger.LogWarning("Tried to add Physics Helper to an object without Physics Component. Destroying component");
                Destroy(this);
                return;
            }
            grabbableObjectRef = physicsComponentRef.grabbableObjectRef;
            cachedType = physicsComponentRef.GetType();
        }
        public bool why = false;
        // wahhh
        void Update()
        {
            if (!grabbableObjectRef.isHeld && ConfigUtil.physicsOnPickup.Value || !physicsComponentRef.alreadyPickedUp) return; //gufdsu guhdsahbdhg
            if (!physicsComponentRef.alreadyPickedUp && !physicsComponentRef.alreadyPickedUp != ConfigUtil.physicsOnPickup.Value && physicsComponentRef.grabbableObjectRef.hasHitGround && why)
            {
                physicsComponentRef.alreadyPickedUp = true;
                physicsComponentRef.enabled = true;
                why = false;
                physicsComponentRef.SetRotation();
            }
            if (!physicsComponentRef.alreadyPickedUp) return;
            if (physicsComponentRef.rigidbody.isKinematic && (!StartOfRound.Instance.shipHasLanded && !StartOfRound.Instance.inShipPhase) && (physicsComponentRef.grabbableObjectRef.isInShipRoom || physicsComponentRef.grabbableObjectRef.isInElevator) && !physicsComponentRef.isPlaced)
            {
                if (!physicsComponentRef.enabled) return;
                physicsComponentRef.enabled = false;
            }
            else
            {
                if (physicsComponentRef.enabled) return;
                physicsComponentRef.enabled = true;
            }
        }
    }
}
