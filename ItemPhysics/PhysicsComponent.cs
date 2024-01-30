#region
using GameNetcodeStuff;
using HarmonyLib;
using Physics_Items.ModCompatFixes;
using Physics_Items.Netcode.NetworkVariables;
using Physics_Items.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.Audio;
using static System.Net.Mime.MediaTypeNames;
using Collision = UnityEngine.Collision;
#endregion

namespace Physics_Items.ItemPhysics
{
    [RequireComponent(typeof(GrabbableObject))]
    public class PhysicsComponent : NetworkBehaviour, IHittable
    {
        public NetworkVariable<PhysicsVariables> net_PhysicsVariable = new NetworkVariable<PhysicsVariables>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public PhysicsVariables local_PhysicsVariable = new PhysicsVariables();
        public GrabbableObject grabbableObjectRef;
        public Collider collider;
        public Rigidbody rigidbody;
        public NetworkTransform networkTransform;
        public NetworkObject networkObject;
        public NetworkRigidbody networkRigidbody;
        public PhysicsHelper physicsHelperRef;
        public Rigidbody scanNodeRigid;
        public float terminalVelocity;
        public float gravity = 9.8f;
        public float throwForce;
        public float oldVolume;
        public bool alreadyPickedUp = false;
        public Vector3 up;
        public float defaultPitch;

        public AudioSource audioSource;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            string _isHost = NetworkUtil.IsServerOrHost ? "Host" : "Client";
            Plugin.Logger.LogWarning($"Network spawn called by: {_isHost}");
            net_PhysicsVariable.OnValueChanged += OnNet_PhysicsVariableChanged;
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            net_PhysicsVariable.OnValueChanged -= OnNet_PhysicsVariableChanged;
        }

        private void OnNet_PhysicsVariableChanged(PhysicsVariables oldValue, PhysicsVariables newValue)
        {
            // Handle the change...
            Plugin.Logger.LogWarning($"NetworkedPhysicsVariable changed from {oldValue} to {newValue}");
            local_PhysicsVariable = newValue;
        }

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
            audioSource = PhysicsUtil.CopyComponent(gameObject.GetComponent<AudioSource>(), gameObject);
            physicsHelperRef = gameObject.AddComponent<PhysicsHelper>();
            collider = gameObject.GetComponent<Collider>();
            oldVolume = audioSource.volume;
            defaultPitch = audioSource.pitch;
            up = grabbableObjectRef.itemProperties.verticalOffset * Vector3.up;
            //grabbableObjectRef.itemProperties.syncDiscardFunction = true; // testing.
            //grabbableObjectRef.itemProperties.syncGrabFunction = true; // testing.
            if (LethalThingsCompatibility.enabled)
            {
                LethalThingsCompatibility.ApplyFixes(this);
            }
            //StartCoroutine(FixAudio());
        }

        private IEnumerator FixAudio()
        {
            yield return new WaitUntil(() => SoundManager.Instance != null && SoundManager.Instance.diageticMixer != null);
            AudioMixerGroup group = SoundManager.Instance.diageticMixer.FindMatchingGroups("Diagetic").FirstOrDefault();
            if(group == null)
            {
                Plugin.Logger.LogWarning("sad");
                yield break;
            }
            audioSource.outputAudioMixerGroup = group;
            Plugin.Logger.LogWarning("fixed audio");
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
                    Material? material = null;
                    if (ConfigUtil.DebuggingStuff.Value)
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
                        if (ConfigUtil.DebuggingStuff.Value) Plugin.Logger.LogWarning($"Found free spot at: {cellCenter}");
                        if(material != null) material.color = Color.yellow;
                        float distance = Vector3.Distance(cellCenter, transform.position);
                        if (distance < minDistance)
                        {
                            if (ConfigUtil.DebuggingStuff.Value) Plugin.Logger.LogWarning($"Found closer distance at: {cellCenter}");
                            minDistance = distance;
                            closestFreeSpot = cellCenter;
                            if (closestFreeSpot == transform.position) break;
                        }
                    }
                }
            }
            if (minDistance != Mathf.Infinity)
            {
                if (ConfigUtil.DebuggingStuff.Value)
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
                if (ConfigUtil.DebuggingStuff.Value && primitives.ContainsKey(closestFreeSpot))
                {
                    Material material = primitives[closestFreeSpot].GetComponent<Renderer>().material;
                    material.color = Color.green;
                }
            }
        }
        public void SetPosition()
        {
            Plugin.Logger.LogWarning("called");
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
            alreadyPickedUp = !ConfigUtil.physicsOnPickup.Value;
            grabbableObjectRef.itemProperties.itemSpawnsOnGround = ConfigUtil.physicsOnPickup.Value;
            //rigidbody.detectCollisions = true;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rigidbody.drag = 0.1f;
            if (apparatusRef != null)
            {
                rigidbody.isKinematic = apparatusRef.isLungDocked || apparatusRef.isLungDockedInElevator || local_PhysicsVariable.isPlaced;
            }
            else
            {
                rigidbody.isKinematic = local_PhysicsVariable.isPlaced; // might mess up the popup notification.
            }

            networkTransform.SyncScaleX = false;
            networkTransform.SyncScaleY = false;
            networkTransform.SyncScaleZ = false;

            networkTransform.enabled = NetworkUtil.ServerHasMod;

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

            addedWeight = false;
            isPushed = false;
            collisions[gameObject] = grabbableObjectRef.itemProperties.weight;
        }

        void UninitializeVariables()
        {
            //grabbableObjectRef.itemProperties.itemSpawnsOnGround = true;
            Plugin.Logger.LogWarning("disable");
            if (!HasRequiredComponents()) return;
            grabbableObjectRef.EnablePhysics(false); // Do this first so the EnablePhysics patch doesn't get skipped.
            Plugin.Instance.skipObject.Add(grabbableObjectRef);
            EnableColliders(true);
            //rigidbody.detectCollisions = false;

            grabbableObjectRef.fallTime = 0f;
            grabbableObjectRef.reachedFloorTarget = false;
            //transform.rotation = Quaternion.Euler(grabbableObjectRef.itemProperties.restingRotation.x, grabbableObjectRef.floorYRot + grabbableObjectRef.itemProperties.floorYOffset + 90f, grabbableObjectRef.itemProperties.restingRotation.z);
            networkTransform.enabled = false;
            firstHit = false;
            hitDir = Vector3.zero;
            oldValue = false;
            addedWeight = false;
            isPushed = false;
        }

        void OnEnable()
        {
            InitializeVariables();
        }

        void OnDisable()
        {
            UninitializeVariables();
        }

        bool HasRequiredComponents()
        {
            if (gameObject == null) return false;
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
        public float calculatedMass;
        public float clampedMass;
        void Start()
        {
            if (!HasRequiredComponents()) return;
            rigidbody.useGravity = false;
            calculatedMass = ((grabbableObjectRef.itemProperties.weight - 1) * 105f); // zeekers why do you calculate mass this way.
            clampedMass = Mathf.Clamp(grabbableObjectRef.itemProperties.weight - 1f, 0f, 10f);
            rigidbody.mass = Mathf.Max(calculatedMass, 1) / 2.205f;
            throwForce = rigidbody.mass * 10f;
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
            if (IsHostOrServer || !NetworkUtil.ServerHasMod)
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

        // TODO: Stop doing dumb shit.
        // TODO: Learn how to use rigidbodies efficiently.
        public bool addedWeight = false;
        protected virtual void Update()
        {
            collisions[gameObject] = grabbableObjectRef.itemProperties.weight;
            if (oldValue != NetworkUtil.ServerHasMod)
            {
                oldValue = NetworkUtil.ServerHasMod;
                Plugin.Logger.LogWarning($"Setting {gameObject} Network Transform enabled to {NetworkUtil.ServerHasMod}");
                networkTransform.enabled = NetworkUtil.ServerHasMod;
            }
            if (apparatusRef != null) // I want to shoot the apparatus with a water gun.
            {
                bool isDocked = apparatusRef.isLungDocked || apparatusRef.isLungDockedInElevator || (!StartOfRound.Instance.shipHasLanded && !StartOfRound.Instance.inShipPhase || local_PhysicsVariable.isPlaced);
                if (rigidbody.isKinematic == isDocked) return;
                rigidbody.isKinematic = isDocked;
            }
            else
            {
                rigidbody.isKinematic = ((grabbableObjectRef.isInShipRoom || grabbableObjectRef.isInElevator) && !StartOfRound.Instance.shipHasLanded && !StartOfRound.Instance.inShipPhase) || local_PhysicsVariable.isPlaced; // TODO: Make items work when ship is moving.
            }
            if (grabbableObjectRef.isInShipRoom || grabbableObjectRef.isInElevator)
            {
                if (rigidbody.isKinematic && !local_PhysicsVariable.isPlaced)
                {
                    /*if (addedWeight)
                    {
                        player.carryWeight -= clampedMass;
                    }*/
                    alreadyPickedUp = false;
                    SetPosition();
                    enabled = false;
                }
            }
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
                var throwForce_ = (5.43f/rigidbody.mass);
                force = hitDir * throwForce_;
                rigidbody.velocity = force;
            }
            if (grabbableObjectRef.itemProperties.dropSFX != null)
            {
                AudioClip clip = grabbableObjectRef.itemProperties.dropSFX;
                if (ConfigUtil.useSourceSounds.Value) clip = ListUtil.GetRandomElement(AssetLoader.allAudioList);
                float? vol = null;
                if (audioSource != null) 
                {
                    if (force != Vector3.zero)
                    {
                        vol = Mathf.Min(force.magnitude, ConfigUtil.maxCollisionVolume.Value);
                    }
                    else
                    {
                        vol = Mathf.Clamp(velocityMag, 0.6f, ConfigUtil.maxCollisionVolume.Value); //Mathf.Min(velocityMag, oldVolume); //oldVolume;
                    }
                    audioSource.volume = vol.Value; //Mathf.Clamp(velocityMag, 0f, audioSource.maxDistance);
                    audioSource.pitch = PhysicsUtil.mapValue(rigidbody.velocity.magnitude, .9f, 10f, .9f, defaultPitch+0.5f);
                    audioSource.PlayOneShot(clip, audioSource.volume);
                    //Plugin.Logger.LogWarning($"Playing with volome {audioSource.pitch}: {audioSource.volume}, {audioSource.minDistance} {audioSource.maxDistance}");
                }
                if (grabbableObjectRef.IsOwner)
                {
                    RoundManager.Instance.PlayAudibleNoise(gameObject.transform.position, vol.HasValue ? vol.Value * 8f : 8f, 0.5f, 0, grabbableObjectRef.isInElevator && StartOfRound.Instance.hangarDoorsClosed, 941);
                }
            }
            grabbableObjectRef.hasHitGround = true;
        }

        bool firstHit = false;
        Vector3 velocity;
        float velocityMag;

        //float sum = 0f;
        //Dictionary<PhysicsComponent, float> cachedCollisions = new Dictionary<PhysicsComponent, float>(); //I know I will figure something out for this
        public Dictionary<GameObject, float> collisions = new Dictionary<GameObject, float>();

        PlayerControllerB player => GameNetworkManager.Instance.localPlayerController;
        float current = 0f;

        // this shit is running EVERY. SINGLE. FRAME. FORLOOPS. NO!!!
        /*void OnCollisionStay(Collision collision)
        {
            //newCollisions.Clear();
            if (blaclist.Contains(collision.gameObject) && collision.gameObject.layer != 26) return;
            collisions.Clear();
            collisions[gameObject] = grabbableObjectRef.itemProperties.weight;
            if (collision.gameObject.layer == 26 && GetPlayer(collision.gameObject) == player)
            {
                isPushed = true;
            }
            foreach (ContactPoint contact in collision.contacts) // I actually big brained on this foreach holy fuck.
            {
                GameObject target = contact.otherCollider.gameObject;
                if (target.CompareTag("PhysicsProp"))
                {
                    if (PhysicsUtil.GetPhysicsComponent(target, out PhysicsComponent collisionPhysComp))
                    {
                        if (collisionPhysComp.grabbableObjectRef.isHeld) continue;
                        if (!collisionPhysComp.collisions.ContainsKey(collisionPhysComp.gameObject)) collisionPhysComp.collisions[collisionPhysComp.gameObject] = collisionPhysComp.grabbableObjectRef.itemProperties.weight;
                        if (!collisionPhysComp.collisions.ContainsKey(gameObject)) collisionPhysComp.collisions[gameObject] = grabbableObjectRef.itemProperties.weight;
                        collisions = collisionPhysComp.collisions;
                        //cachedCollisions = collisions;
                    }
                    else
                    {
                        blaclist.Add(target);
                    }
                }
                if (isPushed)
                {
                    var pushSumWeight = collisions.Sum(x => x.Value); // Get the sum before adding in the players weight.
                    var isCarryRight = Mathf.RoundToInt(Mathf.Clamp((player.carryWeight / 105f) + 1f, 1f, 10f)) == 1;
                    var calculatedSumInZeekers = Mathf.Clamp(pushSumWeight - collisions.Count + 1, 0f, 10f);
                    Plugin.Logger.LogWarning($"{calculatedSumInZeekers}, {player.carryWeight}, {collisions.Count}, {current}");
                }
            }
            //if (collisions.Count <= 1) return;
            *//*foreach (var a in collisions)
            {
                Plugin.Logger.LogWarning($"\n{a.Value}");
            }*//*
        }*/


        public bool isPushed = false;

        Dictionary<GameObject, PlayerControllerB> Players = new Dictionary<GameObject, PlayerControllerB>();
        private PlayerControllerB GetPlayer(GameObject obj)
        {
            if (Players.ContainsKey(obj))
            {
                return Players[obj];
            }
            Players[obj] = obj.GetComponent<PlayerControllerB>();
            if (Players[obj] == null)
            {
                Players[obj] = obj.GetComponentInParent<PlayerControllerB>();
            }
            return Players[obj];
        }

        public void RemoveContact(Collider _collider, bool force=false)
        {
            if (!currentContacts.Contains(_collider) && !force) return;
            currentContacts.Remove(_collider);
            foreach (var contact in currentContacts)
            {
                if(PhysicsUtil.GetPhysicsComponent(contact.gameObject, out PhysicsComponent comp))
                {
                    comp.RemoveContact(_collider);
                }
            }
        }

        void GetContacts(ref Dictionary<GameObject, PhysicsComponent> want)
        {
            foreach (Collider collider in currentContacts)
            {
                GameObject target = collider.gameObject;
                if (target.CompareTag("PhysicsProp"))
                {
                    if (PhysicsUtil.GetPhysicsComponent(target, out PhysicsComponent collisionPhysComp))
                    {
                        if (!want.ContainsKey(target))
                        {
                            want.Add(target, collisionPhysComp);
                            collisionPhysComp.GetContacts(ref want);
                        }
                    }
                }
            }
        }

        public HashSet<Collider> currentContacts = new HashSet<Collider>();

        protected virtual void OnCollisionExit(Collision collision)
        {
            /*if (collision.gameObject.layer == 26 && GetPlayer(collision.gameObject) == player && isPushed)
            {
                isPushed = false;
                if (addedWeight)
                {
                    addedWeight = false;
                    player.carryWeight -= clampedMass;
                    Plugin.Logger.LogWarning(player.carryWeight);
                }
            }*/
            /*if(currentContacts.Count == 1)
            {
                currentContacts.Remove(collider);
            }*/
            if (collision.gameObject.CompareTag("PhysicsProp"))
            {
                RemoveContact(collision.collider);
                /*if(PhysicsUtil.GetPhysicsComponent(collision.gameObject, out PhysicsComponent comp))
                {
                    List<GameObject> compKeys = new List<GameObject>(comp.collisions.Keys);
                    foreach(var key in compKeys)
                    {
                        if (collisions.ContainsKey(key))
                        {
                            if (key == gameObject) continue;
                            collisions.Remove(key);
                        }
                    }
                    if (collisions.ContainsKey(comp.gameObject))
                    {
                        collisions.Remove(comp.gameObject);
                    }
                    Plugin.Logger.LogWarning("remove :3");
                    *//*if (collisions.ContainsKey(comp.gameObject))
                    {
                        collisions.Remove(comp.gameObject);
                        RemoveCollisions(collision);
                    }*//*
                }*/
            }
            if (collision.gameObject.layer == 26 && GetPlayer(collision.gameObject) == player && isPushed)
            {
                Plugin.Logger.LogWarning("not pushed anymore");
                isPushed = false;
                addedWeight = false;
                current = 0f;
            }
        }

        // This is so scuffed
        protected virtual void OnCollisionEnter(Collision collision)
        {
            /*Dictionary<GameObject, PhysicsComponent> test = new Dictionary<GameObject, PhysicsComponent>();
            GetContacts(ref test);
            foreach (var a in test)
            {
                Plugin.Logger.LogWarning(a.Key + ": " + a.Value.ToString());
            }*/
            foreach (ContactPoint contact in collision.contacts)
            {
                currentContacts.Add(contact.otherCollider);
            }
            var test = new Dictionary<GameObject, PhysicsComponent>();
            GetContacts(ref test);
            foreach (var a in test)
            {
                Plugin.Logger.LogWarning($"\n{a.Value.grabbableObjectRef.itemProperties.itemName}");
            }

            if (collision.gameObject.layer == 26 && ConfigUtil.disablePlayerCollision.Value)
            {
                Physics.IgnoreCollision(collider, collision.gameObject.GetComponent<Collider>(), true);
                isPushed = false;
                return;
            }
            // Calculate the force that the player character would experience
            if (collision.gameObject.layer == 26 && GetPlayer(collision.gameObject) == player)
            {
                isPushed = true;
                Plugin.Logger.LogWarning("push started");
            }
            
            if (!firstHit) // So no jumpscare when items first load in xD
            {
                firstHit = true;
                return;
            }
            if (IsHostOrServer)
            {
                /*NetworkObjectReference networkRef = networkObject;
                FastBufferWriter writer = new FastBufferWriter(FastBufferWriter.GetWriteSize(networkRef), Unity.Collections.Allocator.Temp);
                writer.WriteValueSafe(networkRef);
                foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
                {
                    NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(OnCollision.CollisionCheck, client.ClientId, writer, NetworkDelivery.ReliableSequenced);
                }*/
                OnCollisionClientRpc();
            }
            else if(!NetworkUtil.ServerHasMod)
            {
                PlayDropSFX();
            }
            velocity = rigidbody.velocity;
            velocityMag = PhysicsUtil.FastInverseSqrt(velocity.sqrMagnitude);
        }

        [ServerRpc]
        void OnCollisionServerRpc() 
        {
            OnCollisionClientRpc();
        }

        [ClientRpc]
        void OnCollisionClientRpc()
        {
            PlayDropSFX();
        }

        public override void OnDestroy()
        {
            if(HasRequiredComponents()) PhysicsUtil.RemovePhysicsComponent(gameObject);
            base.OnDestroy();
        }

        Vector3? oldPosition;
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
            if (oldPosition.HasValue && grabbableObjectRef.isHeld)
            {
                heldVelocityMagnitudeSqr = (oldPosition.Value - transform.position).sqrMagnitude;
                heldVelocityNormalized = (transform.position-oldPosition.Value).normalized;
            }
            else
            {
                heldVelocityMagnitudeSqr = 0;
                heldVelocityNormalized = Vector3.zero;
            }
            oldPosition = transform.position;
        }
        public float heldVelocityMagnitudeSqr;
        public Vector3 heldVelocityNormalized;

        public bool isHit = false;
        public Vector3 hitDir = Vector3.zero;
        public bool Hit(int force, Vector3 hitDirection, PlayerControllerB playerWhoHit = null, bool playHitSFX = false)
        {
            hitDir = hitDirection;
            isHit = true;
            if (IsHostOrServer)
            {
                
            }
            else if (!NetworkUtil.ServerHasMod)
            {
                PlayDropSFX();
            }
            return true;
        }
    }
}
