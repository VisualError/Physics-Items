using Physics_Items.ItemPhysics;
using System;
using Unity.Netcode;
using UnityEngine;

namespace Physics_Items.Netcode
{
    public class SyncronizedComponentAdder : NetworkBehaviour
    {
        public override void OnNetworkSpawn()
        {
            if (IsServer || IsHost) AddSyncComponent<PhysicsComponent>();
            base.OnNetworkSpawn();
        }
        public void AddSyncComponent<T>() where T : Component
        {
            if (!IsServer||!IsHost) return;
            gameObject.AddComponent<T>();
            AddSyncComponentClientRpc(typeof(T).FullName);
        }

        [ClientRpc]
        private void AddSyncComponentClientRpc(string fullName)
        {
            // Get type by its full name
            Type type = Type.GetType(fullName);
            // Add the component to the GameObject
            gameObject.AddComponent(type);
        }
    }
}
