using System;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;

namespace Physics_Items.ItemPhysics
{
    internal class NetworkedPhysicsComponent : NetworkBehaviour
    {
        void Awake()
        {
            Plugin.Logger.LogWarning("awake");
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Plugin.Logger.LogWarning("spawn");
        }
    }
}
