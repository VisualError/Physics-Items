using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Physics_Items.ItemPhysics.Environment
{
    internal class LandmineController
    {
        static Vector3 previousExplosion = Vector3.zero;
        public static void Init()
        {
            On.Landmine.SpawnExplosion += Landmine_SpawnExplosion;
        }

        private static void Landmine_SpawnExplosion(On.Landmine.orig_SpawnExplosion orig, UnityEngine.Vector3 explosionPosition, bool spawnExplosionEffect, float killRange, float damageRange)
        {

            orig(explosionPosition, spawnExplosionEffect, killRange, damageRange);
            previousExplosion = explosionPosition;
            List<Collider> list = Physics.OverlapSphere(explosionPosition, 6f, 64, QueryTriggerInteraction.Collide).ToList();
            for (int i = 0; i < list.Count; i++)
            {
                Vector3 local = (explosionPosition + Vector3.up) - list[i].transform.position;
                float magnitude = Utils.PhysicsUtil.FastInverseSqrt(local.sqrMagnitude);
                Vector3 normal = (local).normalized;
                if (Utils.PhysicsUtil.GetPhysicsComponent(list[i].gameObject, out PhysicsComponent physics))
                {
                    physics.alreadyPickedUp = true;
                    physics.grabbableObjectRef.EnablePhysics(true);
                    //physics.rigidbody.AddForce(normal * magnitude * 32f, ForceMode.Impulse); // 64 might be more accurate? idk.
                    physics.rigidbody.velocity = local * 80 / magnitude;
                }
            }
        }
    }
}
