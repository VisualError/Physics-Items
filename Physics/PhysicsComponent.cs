using MonoMod.Utils.Cil;
using Physics_Items.NamedMessages;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace Physics_Items.Physics
{
    [RequireComponent(typeof(GrabbableObject))]
    public class PhysicsComponent : MonoBehaviour
    {
        public GrabbableObject grabbableObjectRef;
        public Rigidbody rigidbody;
        public NetworkTransform networkTransform;
        public NetworkObject networkObject;
        public NetworkRigidbody networkRigidbody;
        public bool isPlaced = false;
        public Rigidbody scanNodeRigid;
        public float terminalVelocity;
        public float gravity = 9.8f;
        public float throwForce;
        

        public LungProp lungPropRef; // I have this so I don't have to get component or do an is cast.
        void Awake()
        {
            string a = IsHostOrServer ? "Host" : "Client";
            Plugin.Logger.LogWarning($"Awake called by: {a}");
            grabbableObjectRef = gameObject.GetComponent<GrabbableObject>();
            if(grabbableObjectRef == null)
            {
                Plugin.Logger.LogError($"GrabbableObject component does not exist for Game Object {gameObject}. This is not allowed!");
                return;
            }
            if(!TryGetComponent(out rigidbody))
            {
                rigidbody = gameObject.AddComponent<Rigidbody>();
                rigidbody.collisionDetectionMode = CollisionDetectionMode.Continuous;
            }
            networkObject = GetComponent<NetworkObject>();
            ScanNodeProperties scanNodeProperties = GetComponentInChildren<ScanNodeProperties>();
            if(scanNodeProperties != null && !scanNodeProperties.gameObject.TryGetComponent(out scanNodeRigid))
            {
                scanNodeRigid = scanNodeProperties.gameObject.AddComponent<Rigidbody>();
                Plugin.Logger.LogInfo($"Added Rigidbody to {gameObject} ScanNode");
            }
            if (!TryGetComponent(out networkTransform) && networkObject != null)
            {
                networkTransform = gameObject.AddComponent<NetworkTransform>();
                Plugin.Logger.LogWarning($"Successfully added NetworkTransform to {gameObject}");
            }
            if(!TryGetComponent(out networkRigidbody) && networkObject != null)
            {
                networkRigidbody = gameObject.AddComponent<NetworkRigidbody>();
            }
            if (grabbableObjectRef is LungProp lungProp && lungProp != null)
            {
                lungPropRef = lungProp;
            }
            InitializeVariables();
        }

        void InitializeVariables()
        {
            rigidbody.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rigidbody.drag = 0.1f;
            if (lungPropRef != null)
            {
                rigidbody.isKinematic = lungPropRef.isLungDocked || lungPropRef.isLungDockedInElevator;
            }
            else
            {
                rigidbody.isKinematic = false; // might mess up the popup notification.
            }

            throwForce = rigidbody.mass * 10f;

            networkTransform.SyncScaleX = false;
            networkTransform.SyncScaleY = false;
            networkTransform.SyncScaleZ = false;

            if (scanNodeRigid == null) return;
            scanNodeRigid.isKinematic = true;
            scanNodeRigid.useGravity = false;
        }

        /*public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Plugin.Logger.LogWarning("network spawn called");
            
        }*/

        bool HasRequiredComponents()
        {
            if (rigidbody == null)
            {
                Plugin.Logger.LogError($"Rigidbody doesn't exist for Game Object {gameObject}");
                return false;
            }
            if (networkTransform == null)
            {
                Plugin.Logger.LogError($"Network transform doesn't exist for Game Object {gameObject}");
                return false;
            }
            else if (GetComponent<NetworkObject>() == null)
            {
                Plugin.Logger.LogError($"{gameObject} has Network Transform even though its not a network object.");
                return false;
            }
            if (grabbableObjectRef == null)
            {
                Plugin.Logger.LogError($"{gameObject} Does not have a valid grabbable object component to reference from.");
                return false;
            }
            return true;
        }

        void Start()
        {
            if (!HasRequiredComponents()) return;
            rigidbody.useGravity = false;
            float calculatedMass = ((grabbableObjectRef.itemProperties.weight * 105f) - 105f); // zeekers why do you calculate mass this way.
            rigidbody.mass = Mathf.Max(calculatedMass, 1);
            terminalVelocity = MathF.Sqrt(2 * rigidbody.mass * gravity);
            grabbableObjectRef.itemProperties.itemSpawnsOnGround = false;
        }

        bool IsHostOrServer
        {
            get
            {
                return NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer;
            }
        }
        private Vector3 lastVelocity;
        public float threshold = 0.1f; // Adjust this value based on your needs
        protected virtual void FixedUpdate()
        {
            if (IsHostOrServer || !Plugin.Instance.ServerHasMod)
            {
                if (!rigidbody.isKinematic && !grabbableObjectRef.isHeld)
                {
                    rigidbody.useGravity = false;
                    rigidbody.AddForce(Vector3.down * gravity, ForceMode.Acceleration);
                }
                else
                {
                    rigidbody.AddForce(Vector3.zero, ForceMode.VelocityChange);
                }
            }
            /*Vector3 currentVelocity = rigidbody.velocity;
            if ((currentVelocity - lastVelocity).sqrMagnitude > threshold * threshold)
            {
                Vector3 opposingForce = -rigidbody.velocity * rigidbody.drag * rigidbody.velocity.sqrMagnitude;
                rigidbody.AddForce(opposingForce, ForceMode.Acceleration);
                Plugin.Logger.LogWarning($"Adding opposing force: {opposingForce}");
            }
            lastVelocity = currentVelocity;*/
        }

        public void EnableColliders(bool enable)
        {
            for (int i = 0; i < grabbableObjectRef.propColliders.Length; i++)
            {
                if (!(grabbableObjectRef.propColliders[i] == null) && !grabbableObjectRef.propColliders[i].gameObject.CompareTag("InteractTrigger") && !grabbableObjectRef.propColliders[i].gameObject.CompareTag("DoNotSet"))
                {
                    grabbableObjectRef.propColliders[i].enabled = enable;
                }
            }
        }

        bool oldValue;
        private Transform pos;
        private Vector3? relativePosition;
        private Vector3 oldPos;
        private Vector3 oldRelativePosition;
        private float magnitude;

        private void MoveWithParent()
        {

        }

        protected virtual void Update()
        {
            if(oldValue != Plugin.Instance.ServerHasMod)
            {
                oldValue = Plugin.Instance.ServerHasMod;
                Plugin.Logger.LogWarning($"Setting {gameObject} Network Transform enabled to {Plugin.Instance.ServerHasMod}");
                networkTransform.enabled = Plugin.Instance.ServerHasMod;
            }
            if ((grabbableObjectRef.isInShipRoom || grabbableObjectRef.isInElevator))
            {
                rigidbody.isKinematic = !StartOfRound.Instance.shipHasLanded && !StartOfRound.Instance.inShipPhase || isPlaced; // Ill fix this eventually
            }
            if(networkTransform.enabled != !grabbableObjectRef.isHeld && Plugin.Instance.ServerHasMod)
            {
                networkTransform.enabled = !grabbableObjectRef.isHeld;
            }
            if (lungPropRef != null)
            {
                bool isDocked = lungPropRef.isLungDocked || lungPropRef.isLungDockedInElevator;
                if (rigidbody.isKinematic == isDocked) return;
                rigidbody.isKinematic = lungPropRef.isLungDocked || lungPropRef.isLungDockedInElevator;
            }
        }

        public void PlayDropSFX()
        {
            grabbableObjectRef.PlayDropSFX();
        }

        // This is so scuffed
        protected virtual void OnCollisionEnter(Collision collision)
        {
            if (IsHostOrServer)
            {
                int id = gameObject.GetInstanceID();
                FastBufferWriter writer = new FastBufferWriter(FastBufferWriter.GetWriteSize(id), Unity.Collections.Allocator.Temp);
                writer.WriteValueSafe(id);
                foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
                {
                    NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(OnCollision.CollisionCheck, client.ClientId, writer, NetworkDelivery.ReliableSequenced);
                }
            }
            else if(!Plugin.Instance.ServerHasMod)
            {
                PlayDropSFX();
            }
        }

        void OnDestroy()
        {
            Utils.Physics.RemovePhysicsComponent(gameObject);
        }

        protected virtual void LateUpdate()
        {
            if (grabbableObjectRef == null)
            {
                Plugin.Logger.LogError($"{gameObject} Does not have a valid grabbable object component to reference from.");
                return;
            }
            if (grabbableObjectRef.parentObject != null && (grabbableObjectRef.parentObject))
            {
                transform.rotation = grabbableObjectRef.parentObject.rotation;
                Vector3 rotationOffset = grabbableObjectRef.itemProperties.rotationOffset;
                transform.Rotate(rotationOffset);
                transform.position = grabbableObjectRef.parentObject.position;
                Vector3 positionOffset = grabbableObjectRef.itemProperties.positionOffset;
                positionOffset = grabbableObjectRef.parentObject.rotation * positionOffset;
                transform.position += positionOffset;
            }
            if (grabbableObjectRef.radarIcon != null)
            {
                grabbableObjectRef.radarIcon.position = transform.position;
            }
        }

    }
}
