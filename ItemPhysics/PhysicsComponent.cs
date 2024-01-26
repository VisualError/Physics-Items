using GameNetcodeStuff;
using MoreShipUpgrades.Patches;
using Physics_Items.ModCompatFixes;
using Physics_Items.NamedMessages;
using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using static UnityEngine.ParticleSystem.PlaybackState;
using Collision = UnityEngine.Collision;

namespace Physics_Items.ItemPhysics
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
        public bool alreadyPickedUp = false;
        public Vector3 up;

        public AudioSource audioSource;

        public LungProp apparatusRef; // I have this so I don't have to get component or do an is cast.
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
                apparatusRef = lungProp;
            }
            audioSource = gameObject.GetComponent<AudioSource>();
            physicsHelperRef = gameObject.AddComponent<PhysicsHelper>();
            collider = gameObject.GetComponent<Collider>();
            oldVolume = audioSource.volume;
            throwForce = rigidbody.mass * 10f;
            up = grabbableObjectRef.itemProperties.verticalOffset * Vector3.up;
            grabbableObjectRef.itemProperties.syncDiscardFunction = true; // testing.
            if (LethalThingsCompatibility.enabled)
            {
                LethalThingsCompatibility.ApplyFixes(this);
            }
        }
        public int gridSize = 5;
        public float cellSize = 2f;
        public Collider[] results = new Collider[3];

        public void FixPosition()
        {
            if (rigidbody.isKinematic) return;
            Dictionary<Vector3, GameObject> primitives = new Dictionary<Vector3, GameObject>();
            Vector3 closestFreeSpot = Vector3.zero;
            float minDistance = Mathf.Infinity;
            // Populate the grid
            for (int x = 0; x < gridSize; x++)
            {
                for (int y = 0; y < gridSize; y++)
                {
                    // Calculate the center of the cell
                    Vector3 cellCenter = new Vector3((x - gridSize / 2) * cellSize /2, 0, (y - gridSize / 2) * cellSize/2) + new Vector3(cellSize, 0, cellSize) * 0.5f;
                    if (cellCenter == new Vector3(cellSize, 0, cellSize) * 0.5f)
                    {
                        cellCenter = Vector3.zero;
                    }
                    // Calculate the half extents of the box
                    Vector3 halfExtents = new Vector3(cellSize / 2, cellSize / 2, cellSize / 2);

                    // Add the item's position to the cell center
                    cellCenter += transform.position;
                    Material material = null;
                    if (Plugin.Instance.DebuggingStuff.Value)
                    {
                        primitives[cellCenter] = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        primitives[cellCenter].layer = 6;
                        primitives[cellCenter].transform.position = cellCenter;
                        primitives[cellCenter].transform.localScale = halfExtents;
                        primitives[cellCenter].GetComponent<Collider>().isTrigger = true;
                        primitives[cellCenter].GetComponent<Collider>().enabled = false;
                        Plugin.Logger.LogInfo($"Found cell: {cellCenter}");
                        material = primitives[cellCenter].GetComponent<Renderer>().material;
                        material.shader = Shader.Find("HDRP/Lit");
                    }
                    // Check if there's an obstacle at the cell center
                    int overlap = Physics.OverlapBoxNonAlloc(cellCenter, halfExtents, results, Quaternion.identity, 2318, QueryTriggerInteraction.Ignore);
                    if (overlap <= 1 || overlap <= 0)
                    {
                        if (Plugin.Instance.DebuggingStuff.Value) Plugin.Logger.LogWarning($"Found free spot at: {cellCenter}");
                        if(material != null) material.color = Color.yellow;
                        float distance = Vector3.Distance(cellCenter, transform.position);
                        if (distance < minDistance)
                        {
                            if (Plugin.Instance.DebuggingStuff.Value) Plugin.Logger.LogWarning($"Found closer distance at: {cellCenter}");
                            minDistance = distance;
                            closestFreeSpot = cellCenter;
                            if (closestFreeSpot == transform.position) break;
                        }
                    }
                }
            }
            if (minDistance != Mathf.Infinity)
            {
                if (Plugin.Instance.DebuggingStuff.Value)
                {
                    Plugin.Logger.LogWarning($"Moving item to closest free spot at: {closestFreeSpot}");
                    GameObject originalPosition = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    originalPosition.layer = 6;
                    originalPosition.transform.position = transform.position;
                    originalPosition.transform.localScale = new Vector3(cellSize / 2, cellSize / 2, cellSize / 2);
                    var testMaterial = originalPosition.GetComponent<Renderer>().material;
                    testMaterial.shader = Shader.Find("HDRP/Lit");
                    testMaterial.color = Color.blue;
                    testMaterial.color = new Color(testMaterial.color.r, testMaterial.color.g, testMaterial.color.b, 0.5f); // 50% transparency
                    originalPosition.GetComponent<Collider>().isTrigger = true;
                    originalPosition.GetComponent<Collider>().enabled = false;
                }
                transform.position = closestFreeSpot;
                if (Plugin.Instance.DebuggingStuff.Value && primitives.ContainsKey(closestFreeSpot))
                {
                    Material material = primitives[closestFreeSpot].GetComponent<Renderer>().material;
                    material.color = Color.green;
                }
            }
        }
        public void SetPosition()
        {
            Transform parent = GetParent();
            if (parent != transform)
            {
                Vector3 relativePosition = parent.InverseTransformPoint(transform.position);
                transform.localPosition = relativePosition; //Vector3.Lerp(transform.localPosition, relativePosition + up, 0.2f);
            }
        }
        public void SetRotation()
        {
            if (grabbableObjectRef.floorYRot == -1)
            {
                transform.rotation = Quaternion.Euler(grabbableObjectRef.itemProperties.restingRotation.x, grabbableObjectRef.transform.eulerAngles.y, grabbableObjectRef.itemProperties.restingRotation.z);
            }
            else
            {
                transform.rotation = Quaternion.Euler(grabbableObjectRef.itemProperties.restingRotation.x, grabbableObjectRef.floorYRot + grabbableObjectRef.itemProperties.floorYOffset + 90f, grabbableObjectRef.itemProperties.restingRotation.z);
            }
        }

        void InitializeVariables()
        {

            //SetPosition();
            alreadyPickedUp = !Plugin.Instance.physicsOnPickup.Value;
            grabbableObjectRef.itemProperties.itemSpawnsOnGround = Plugin.Instance.physicsOnPickup.Value;
            //rigidbody.detectCollisions = true;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rigidbody.drag = 0.1f;
            if (apparatusRef != null)
            {
                rigidbody.isKinematic = apparatusRef.isLungDocked || apparatusRef.isLungDockedInElevator || isPlaced;
            }
            else
            {
                rigidbody.isKinematic = isPlaced; // might mess up the popup notification.
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
            rigidbody.velocity = Vector3.zero;
            FixPosition();
            rigidbody.velocity = Vector3.zero;
        }

        void UninitializeVariables()
        {
            //grabbableObjectRef.itemProperties.itemSpawnsOnGround = true;
            Plugin.Logger.LogWarning("disable");
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
            InitializeVariables();
        }

        void OnDisable()
        {
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

        bool oldValue = false;
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
            if(slow)
            {
                GameNetworkManager.Instance.localPlayerController.isMovementHindered = (int)(rigidbody.mass / 1f) - 1;
            }
            if (oldValue != Plugin.Instance.ServerHasMod)
            {
                oldValue = Plugin.Instance.ServerHasMod;
                Plugin.Logger.LogWarning($"Setting {gameObject} Network Transform enabled to {Plugin.Instance.ServerHasMod}");
                networkTransform.enabled = Plugin.Instance.ServerHasMod;
            }
            if (apparatusRef != null) // I want to shoot the apparatus with a water gun.
            {
                bool isDocked = apparatusRef.isLungDocked || apparatusRef.isLungDockedInElevator || (!StartOfRound.Instance.shipHasLanded && !StartOfRound.Instance.inShipPhase || isPlaced);
                if (rigidbody.isKinematic == isDocked) return;
                rigidbody.isKinematic = isDocked;
            }
            else
            {
                rigidbody.isKinematic = ((grabbableObjectRef.isInShipRoom || grabbableObjectRef.isInElevator) && !StartOfRound.Instance.shipHasLanded && !StartOfRound.Instance.inShipPhase) || isPlaced; // TODO: Make items work when ship is moving.
            }
            if (grabbableObjectRef.isInShipRoom || grabbableObjectRef.isInElevator)
            {
                if (rigidbody.isKinematic && !isPlaced)
                {
                    alreadyPickedUp = false;
                    SetPosition();
                    enabled = false;
                }
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
                rigidbody.velocity = force;
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
                        vol = Mathf.Min(force.magnitude, Plugin.Instance.maxCollisionVolume.Value);
                        Plugin.Logger.LogWarning("wat");
                    }
                    else
                    {
                        vol = Mathf.Clamp(velocityMag, 0.2f, Plugin.Instance.maxCollisionVolume.Value); //Mathf.Min(velocityMag, oldVolume); //oldVolume;
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

        protected virtual void OnCollisionExit(Collision collision)
        {
            if (collision.gameObject.layer == 26)
            {
                slow = false;
                GameNetworkManager.Instance.localPlayerController.isMovementHindered = 0;
            }
        }


        public bool slow;
        // This is so scuffed
        protected virtual void OnCollisionEnter(Collision collision)
        {
            if (collision.gameObject.layer == 26 && Plugin.Instance.disablePlayerCollision.Value)
            {
                Physics.IgnoreCollision(collider, collision.gameObject.GetComponent<Collider>(), true); // test
                return;
            }
            // Calculate the force that the player character would experience
            if (collision.gameObject.layer == 26)
            {
                slow = true;
            }
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
            hitDir = hitDirection;
            isHit = true;
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
            else if (!Plugin.Instance.ServerHasMod)
            {
                PlayDropSFX();
            }
            return true;
        }
    }
}
