using Physics_Items.Physics;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Physics_Items.Utils
{
    internal class Physics
    {
        internal static Dictionary<GameObject, PhysicsComponent> physicsComponents = new Dictionary<GameObject, PhysicsComponent>();
        internal static PhysicsComponent GetPhysicsComponent(GameObject gameObj)
        {
            if (physicsComponents.ContainsKey(gameObj)) return physicsComponents[gameObj];
            return gameObj.GetComponent<PhysicsComponent>();
        }

        internal static PhysicsComponent GetPhysicsComponent(GameObject gameObj, out PhysicsComponent physicsComponent)
        {
            if (physicsComponents.ContainsKey(gameObj))
            {
                physicsComponent = physicsComponents[gameObj];
            }
            else
            {
                physicsComponent = gameObj.GetComponent<PhysicsComponent>();
                physicsComponents.Add(gameObj, physicsComponent);
            }
            return physicsComponent;
        }

        internal static bool RemovePhysicsComponent(GameObject gameObj)
        {
            if (!physicsComponents.ContainsKey(gameObj)) return false;
            physicsComponents.Remove(gameObj);
            return true;
        }
    }
}
