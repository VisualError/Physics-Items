using Physics_Items.ItemPhysics;
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

        internal static bool GetPhysicsComponent(GameObject gameObj, out PhysicsComponent physicsComponent)
        {
            if (physicsComponents.ContainsKey(gameObj))
            {
                physicsComponent = physicsComponents[gameObj];
            }
            else
            {
                physicsComponent = gameObj.GetComponent<PhysicsComponent>();
                physicsComponents[gameObj] = physicsComponent;
            }
            return physicsComponent != null;
        }

        internal static bool RemovePhysicsComponent(GameObject gameObj)
        {
            if (!physicsComponents.ContainsKey(gameObj)) return false;
            physicsComponents.Remove(gameObj);
            return true;
        }
    }
}
