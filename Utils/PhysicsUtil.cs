using GameNetcodeStuff;
using Physics_Items.ItemPhysics;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Physics_Items.Utils
{
    internal class PhysicsUtil
    {
        internal static Dictionary<GameObject, PhysicsComponent> physicsComponents = new Dictionary<GameObject, PhysicsComponent>();
        //internal static Dictionary<GameObject, PhysicsComponent> networkedPhysicComponents = new Dictionary<GameObject, Networkp>();
        internal static List<PlayerControllerB> playerControllerBs = new List<PlayerControllerB>();
        internal static PhysicsComponent GetPhysicsComponent(GameObject gameObj)
        {
            if (physicsComponents.ContainsKey(gameObj)) return physicsComponents[gameObj];
            return gameObj.GetComponent<PhysicsComponent>();
        }

        private const float MinThrowForce = 36f;
        private const float MaxForceMultiplier = 10f;

        public static float CalculateDistance(Vector3 startPosition, Vector3 targetPosition)
        {
            return (startPosition - targetPosition).magnitude;
        }

        // TODO: Optimize code
        public static PhysicsComponent? AddPhysicsComponent(GrabbableObject grabbableObject)
        {
            if (Plugin.Instance.blockList.Contains(grabbableObject.GetType()))
            {
                if (ConfigUtil.overrideAllItemPhysics.Value) return null;
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

        public static Vector3 CalculateDirection(Vector3 targetPosition, Vector3 startPosition)
        {
            return (targetPosition - startPosition).normalized;
        }

        public static bool IsThrown(float distance)
        {
            return distance > 1f;
        }

        public static float CalculateThrowForce(bool isThrown, float throwForce)
        {
            return isThrown ? Mathf.Min(throwForce, MinThrowForce) : 0f;
        }

        public static float CalculateForceMultiplier(bool isThrown, float distance, float throwForce, float mass)
        {
            return isThrown ? Mathf.Min(distance * throwForce, mass * MaxForceMultiplier) : 0f;
        }

        public static Vector3 CalculateForce(float forceMultiplier, Vector3 direction, PhysicsComponent comp, bool isThrown)
        {
            Vector3 baseForce = isThrown ? direction * forceMultiplier : Vector3.zero;
            Vector3 velocityForce = comp.heldVelocityNormalized * ConfigUtil.DiscardFollowAmplitude.Value * comp.rigidbody.mass * Utils.PhysicsUtil.FastInverseSqrt(comp.heldVelocityMagnitudeSqr);
            return baseForce + velocityForce;
        }

        public static void ApplyForce(PhysicsComponent comp, Vector3 force)
        {
            comp.rigidbody.AddForce(force, ForceMode.Impulse);
        }

        public static float FastInverseSqrt(float number)
        {
            int i;
            float x2, y;
            const float threehalfs = 1.5F;

            x2 = number * 0.5F;
            y = number;
            i = BitConverter.ToInt32(BitConverter.GetBytes(y), 0);
            i = 0x5f3759df - (i >> 1);
            y = BitConverter.ToSingle(BitConverter.GetBytes(i), 0);
            y = y * (threehalfs - (x2 * y * y));

            return 1 / y;
        }

        public static T CopyComponent<T>(T original, GameObject destination) where T : Component
        {
            Type type = original.GetType();
            var dst = destination.AddComponent(type) as T;
            var fields = type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var field in fields)
            {
                if (field.IsStatic) continue;
                field.SetValue(dst, field.GetValue(original));
            }
            var props = type.GetProperties();
            foreach (var prop in props)
            {
                if (!prop.CanWrite || !prop.CanWrite || prop.Name == "name" || prop.IsDefined(typeof(ObsoleteAttribute), true)) continue;
                prop.SetValue(dst, prop.GetValue(original, null), null);
            }
            return dst as T;
        }

        public static float mapValue(float mainValue, float inValueMin, float inValueMax, float outValueMin, float outValueMax)
        {
            return (mainValue - inValueMin) * (outValueMax - outValueMin) / (inValueMax - inValueMin) + outValueMin;
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
