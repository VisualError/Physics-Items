using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Physics_Items.Physics
{
    // TODO: Possibly turn this into a handler for RPC related shit, make it a network behaviour and somehow register the prefab without fucking up joining servers that doesnt have the mod.
    public class PhysicsHelper : MonoBehaviour
    {
        PhysicsComponent physicsComponentRef;
        void Awake()
        {
            if(!TryGetComponent(out physicsComponentRef))
            {
                Plugin.Logger.LogWarning("Tried to add Physics Helper to an object without Physics Component. Destroying component");
                Destroy(this);
                return;
            }
        }
        // wahhh
        void Update()
        {
            if(physicsComponentRef.rigidbody.isKinematic && (!StartOfRound.Instance.shipHasLanded && !StartOfRound.Instance.inShipPhase) && (physicsComponentRef.grabbableObjectRef.isInShipRoom || physicsComponentRef.grabbableObjectRef.isInElevator))
            {
                physicsComponentRef.enabled = false;
            }
            else
            {
                physicsComponentRef.enabled = true;
            }
        }
    }
}
