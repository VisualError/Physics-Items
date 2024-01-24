using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Physics_Items.ItemPhysics.Environment
{
    internal class Landmine
    {
        public static void Init()
        {
            On.Landmine.SpawnExplosion += Landmine_SpawnExplosion;
        }

        private static void Landmine_SpawnExplosion(On.Landmine.orig_SpawnExplosion orig, UnityEngine.Vector3 explosionPosition, bool spawnExplosionEffect, float killRange, float damageRange)
        {
            orig(explosionPosition, spawnExplosionEffect, killRange, damageRange);
            List<Collider> list = Physics.OverlapSphere(explosionPosition, 6f, 64, QueryTriggerInteraction.Collide).ToList();
            for (int i = 0; i < list.Count; i++)
            {
                float magnitude = PhysicsComponent.FastInverseSqrt((explosionPosition - list[i].transform.position).sqrMagnitude);
                Vector3 normal = (list[i].transform.position - explosionPosition).normalized;
                if (Utils.Physics.GetPhysicsComponent(list[i].gameObject, out PhysicsComponent physics))
                {
                    physics.rigidbody.AddForce(normal * magnitude * 16f, ForceMode.Impulse);
                }
            }
        }
    }
}
