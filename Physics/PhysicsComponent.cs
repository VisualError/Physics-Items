using GameNetcodeStuff;
using MonoMod.Utils.Cil;
using Physics_Items.NamedMessages;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.Experimental.Audio;

namespace Physics_Items.Physics
{
    [RequireComponent(typeof(GrabbableObject))]
    public class PhysicsComponent : MonoBehaviour, IHittable
    {
        public GrabbableObject grabbableObjectRef;
        public Collider collider;
        public Rigidbody rigidbody;
        public NetworkTransform networkTransform;
        public NetworkObject networkObject;
        public NetworkRigidbody networkRigidbody;
        public PhysicsHelper physicsHelperRef;
        public bool isPlaced = false;
        public Rigidbody scanNodeRigid;
        public float terminalVelocity;
        public float gravity = 9.8f;
        public float throwForce;
        public float oldVolume;
        public Vector3 up;

        public AudioSource audioSource;

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
            audioSource = gameObject.GetComponent<AudioSource>();
            physicsHelperRef = gameObject.AddComponent<PhysicsHelper>();
            collider = gameObject.GetComponent<Collider>();
            oldVolume = audioSource.volume;
            throwForce = rigidbody.mass * 10f;
            up = grabbableObjectRef.itemProperties.verticalOffset * Vector3.up;
        }

        void SetPosition()
        {
            Transform parent = GetParent();
            if (parent != transform)
            {
                Vector3 relativePosition = parent.InverseTransformPoint(transform.position + up);
                transform.localPosition = relativePosition;
            }
        }

        void InitializeVariables()
        {

            SetPosition();

            grabbableObjectRef.itemProperties.itemSpawnsOnGround = false;
            //rigidbody.detectCollisions = true;
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

            networkTransform.SyncScaleX = false;
            networkTransform.SyncScaleY = false;
            networkTransform.SyncScaleZ = false;

            networkTransform.enabled = Plugin.Instance.ServerHasMod;

            grabbableObjectRef.fallTime = 1f;
            grabbableObjectRef.reachedFloorTarget = true;
            Plugin.Instance.skipObject.Remove(grabbableObjectRef);

            if (scanNodeRigid != null)
            {
                scanNodeRigid.isKinematic = true;
                scanNodeRigid.useGravity = false;
            }
        }

        void UninitializeVariables()
        {
            //grabbableObjectRef.itemProperties.itemSpawnsOnGround = true;
            grabbableObjectRef.EnablePhysics(false); // Do this first so the EnablePhysics patch doesn't get skipped.
            Plugin.Instance.skipObject.Add(grabbableObjectRef);
            EnableColliders(true);
            //rigidbody.detectCollisions = false;

            grabbableObjectRef.fallTime = 0f;
            grabbableObjectRef.reachedFloorTarget = false;
            audioSource.volume = oldVolume;
            //transform.rotation = Quaternion.Euler(grabbableObjectRef.itemProperties.restingRotation.x, grabbableObjectRef.floorYRot + grabbableObjectRef.itemProperties.floorYOffset + 90f, grabbableObjectRef.itemProperties.restingRotation.z);
            networkTransform.enabled = false;
            firstHit = false;
            hitDir = Vector3.zero;
            oldValue = false;
        }

        void OnEnable()
        {
            Plugin.Logger.LogWarning("enabled");
            InitializeVariables();
        }

        void OnDisable()
        {
            Plugin.Logger.LogWarning("disabled");
            UninitializeVariables();
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

            if (StartOfRound.Instance.inShipPhase)
            {
                grabbableObjectRef.fallTime = 1f;
                grabbableObjectRef.hasHitGround = true;
                grabbableObjectRef.scrapPersistedThroughRounds = true;
                grabbableObjectRef.isInElevator = true;
                grabbableObjectRef.isInShipRoom = true;
            }
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
        private Transform parent;
        private Vector3? relativePosition;
        private Vector3 oldPos;
        private Vector3 oldRelativePosition;
        private float magnitude;

        private void MoveWithParent()
        {

        }

        // TODO: Stop doing dumb shit.
        // TODO: Learn how to use rigidbodies efficiently.
        protected virtual void Update()
        {
            if(oldValue != Plugin.Instance.ServerHasMod)
            {
                oldValue = Plugin.Instance.ServerHasMod;
                Plugin.Logger.LogWarning($"Setting {gameObject} Network Transform enabled to {Plugin.Instance.ServerHasMod}");
                networkTransform.enabled = Plugin.Instance.ServerHasMod;
            }
            if (grabbableObjectRef.isInShipRoom || grabbableObjectRef.isInElevator)
            {
                rigidbody.isKinematic = !StartOfRound.Instance.shipHasLanded && !StartOfRound.Instance.inShipPhase || isPlaced; // TODO: Make items work when ship is moving.
                if (rigidbody.isKinematic)
                {
                    SetPosition();
                    enabled = false;
                }
            }
            if (lungPropRef != null)
            {
                bool isDocked = lungPropRef.isLungDocked || lungPropRef.isLungDockedInElevator || (!StartOfRound.Instance.shipHasLanded && !StartOfRound.Instance.inShipPhase || isPlaced);
                if (rigidbody.isKinematic == isDocked) return;
                rigidbody.isKinematic = isDocked;
            }
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

        public Transform GetParent()
        {
            if (grabbableObjectRef.parentObject != null)
            {
                parent = grabbableObjectRef.parentObject;
            }
            else if (transform.parent != null)
            {
                parent = transform.parent;
            }
            else
            {
                parent = transform;
            }
            return parent;
        }

        public void PlayDropSFX()
        {
            var force = Vector3.zero;
            if (isHit)
            {
                isHit = false;
                var throwForce_ = Mathf.Min(throwForce, 36f);
                var mult = Mathf.Min(throwForce_, rigidbody.mass * 10);
                force = hitDir * mult;
                rigidbody.AddForce(force, ForceMode.Impulse);
            }
            if (grabbableObjectRef.itemProperties.dropSFX != null)
            {
                AudioClip clip = grabbableObjectRef.itemProperties.dropSFX;
                if (Plugin.Instance.useSourceSounds.Value) clip = Utils.ListUtil.GetRandomElement(Utils.AssetLoader.allAudioList);
                if (audioSource != null) 
                {
                    float vol;
                    if (force != Vector3.zero)
                    {
                        vol = Mathf.Min(force.magnitude, oldVolume);
                        Plugin.Logger.LogWarning("wat");
                    }
                    else
                    {
                        vol = Mathf.Min(velocityMag, oldVolume); //oldVolume;
                    }
                    audioSource.volume = vol; //Mathf.Clamp(velocityMag, 0f, audioSource.maxDistance);
                    audioSource.PlayOneShot(clip, audioSource.volume);
                    //Plugin.Logger.LogWarning($"Playing with volome: {audioSource.volume}, {audioSource.minDistance} {audioSource.maxDistance}");
                }
                if (grabbableObjectRef.IsOwner)
                {
                    RoundManager.Instance.PlayAudibleNoise(gameObject.transform.position, 8f, 0.5f, 0, grabbableObjectRef.isInElevator && StartOfRound.Instance.hangarDoorsClosed, 941);
                }
            }
            grabbableObjectRef.hasHitGround = true;
        }

        static bool firstHit = false;
        static Vector3 velocity;
        static float velocityMag;
        // This is so scuffed
        protected virtual void OnCollisionEnter(Collision collision)
        {
            if (!firstHit) // So no jumpscare when items first load in xD
            {
                firstHit = true;
                return;
            }
            if (IsHostOrServer)
            {
                NetworkObjectReference networkRef = networkObject;
                FastBufferWriter writer = new FastBufferWriter(FastBufferWriter.GetWriteSize(networkRef), Unity.Collections.Allocator.Temp);
                writer.WriteValueSafe(networkRef);
                foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
                {
                    NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(OnCollision.CollisionCheck, client.ClientId, writer, NetworkDelivery.ReliableSequenced);
                }
            }
            else if(!Plugin.Instance.ServerHasMod)
            {
                PlayDropSFX();
            }
            velocity = rigidbody.velocity;
            velocityMag = FastInverseSqrt(velocity.sqrMagnitude);
        }

        void OnDestroy()
        {
            Utils.Physics.RemovePhysicsComponent(gameObject);
        }

        protected virtual void LateUpdate()
        {
            if (grabbableObjectRef == null)
            {
                Plugin.Logger.LogError($"{gameObject} Does not have a valid grabbable object component to reference from. Destroying script.");
                Destroy(this);
                return;
            }
            if (grabbableObjectRef.parentObject != null && grabbableObjectRef.isHeld)
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

        public bool isHit = false;
        public Vector3 hitDir = Vector3.zero;
        public bool Hit(int force, Vector3 hitDirection, PlayerControllerB playerWhoHit = null, bool playHitSFX = false)
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
            else if (!Plugin.Instance.ServerHasMod)
            {
                PlayDropSFX();
            }
            hitDir = hitDirection;
            isHit = true;
            return true;
        }
    }
}
