using HarmonyLib;
using Physics_Items.Physics;
using Unity.Netcode;
using UnityEngine;

namespace Physics_Items.NamedMessages
{
    internal class OnCollision
    {
        internal static string CollisionCheck = "PhysicsItemsCollisionCheck";
        [HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.Awake)), HarmonyPrefix]
        public static void StartOfRound_Awake()
        {
            NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(CollisionCheck, OnReceive);
        }

        [HarmonyPatch(typeof(GameNetworkManager), nameof(GameNetworkManager.Disconnect)), HarmonyPrefix]
        public static void OnDestroy()
        {
            NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(CollisionCheck);
            Utils.Physics.physicsComponents.Clear();
        }

        private static void OnReceive(ulong senderClientId, FastBufferReader messagePayload)
        {
            int value;
            messagePayload.ReadValueSafe(out value);
            Object got = GameObject.FindObjectFromInstanceID(value);
            if(got is GameObject obj && obj != null)
            {
                PhysicsComponent physComp = Utils.Physics.GetPhysicsComponent(obj);
                if(physComp != null)
                {
                    physComp.PlayDropSFX();
                }
            }
        }
    }
}
