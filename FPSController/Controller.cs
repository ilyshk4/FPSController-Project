using Modding;
using Modding.Blocks;
using System;
using UnityEngine;
using LocalMachine = Machine;

namespace FPSController
{
    public class Controller : BlockScript, IExplosionEffect
    {
        public static Controller Current;

        public MeshRenderer meshRenderer;

        public Vector3 inputDirection = Vector3.forward;
        public Vector3 inputRotation = Vector3.zero;

        public float rotationX, rotationY;
        public float targetRotationX, targetRotationY;

        public MSlider fov;
        public MSlider zoomFov;
        public MSlider speed;
        public MSlider acceleration;
        public MSlider sensitivity;
        public MSlider smoothing;
        public MSlider groundStickDistance;
        public MSlider interactDistance;
        public MSlider jumpForce;
        public MSlider mass;
        public MSlider healthSlider;
        public MSlider minimumDamage;
        public MSlider pushForceScale;

        public MKey activateKey;
        public MKey controlled;
        public MKey jump;
        public MKey interact;
        public MKey grab;
        public MKey crouch;

        public MLimits pitchLimits;

        public MToggle alwaysLabel;
        public MToggle visualPitch;
        public MToggle toggleCrouch;
        public MToggle healthToggle;

        public Interactable lookingAt;
        public Interactable interactingWith;

        public SphereCollider top;
        public CapsuleCollider bottom;

        public AudioSource audioSource;

        public ParticleSystem bloodBurst;
        public ParticleSystem bloodBurstHit;
        public ParticleSystem bloodSquirt;
        public Transform bloodQuad;

        public Rigidbody grabbed;
        public SpringJoint grabJoint;

        public Seat seat;

        public bool controlling;
        public bool isGrounded;
        public bool targetCrouching;
        public bool clientControlling;

        public float vertical, horizontal;

        public bool IsSitting => seat != null;
        public bool IsLocal => Machine.InternalObject == LocalMachine.Active() || !StatMaster.isMP;
        public bool HasAuthority => Mod.HasAuthority;
        public bool IsFixedCameraActive => FixedCameraController.Instance.activeCamera;

        private Camera _viewCamera;
        public Camera MainCamera
        {
            get
            {
                if (_viewCamera == null && MouseOrbit.Instance?.cam != null)
                    _viewCamera = MouseOrbit.Instance.cam;
                if (_viewCamera == null)
                    _viewCamera = Camera.main;
                return _viewCamera;
            }
        }

        private Camera _hudCamera;
        public Camera HUDCamera
        {
            get
            {
                if (_hudCamera == null && MouseOrbit.Instance?.hud3Dcam != null)
                    _hudCamera = MouseOrbit.Instance.hud3Dcam;
                if (_hudCamera == null)
                    _hudCamera = Camera.main.transform.GetChild(0).GetComponent<Camera>();
                return _hudCamera;
            }
        }

        public BlockHealthBar Health => BlockBehaviour.BlockHealth;

        public Vector3 CrouchOffset => _crouching ? Vector3.up : Vector3.zero;

        public bool Dead => Health.health <= 0;

        private float _zoom;
        private float _oldFov;
        private float _oldNearClip;
        private long _localTicksCount = 0;
        private Quaternion _finalRotation;
        private Quaternion _lastSittingRotation;
        private Quaternion _lookRotation;
        private Quaternion _lastLookRotation;
        private Quaternion _visualsRotation;
        private Vector3 _visualsPosition;
        private bool _died;
        private bool _previousControlled;
        private float _previousHealth;
        private RaycastHit[] _hits;

        private bool _crouching;

        private static Quaternion ViewOffset = Quaternion.AngleAxis(-90, -Vector3.right);

        private static int ControllerCollisionMask = Game.BlockEntityLayerMask | 1 | 1 << 29;
        public override bool EmulatesAnyKeys => true;

        private void Start()
        {
            foreach (var joint in GetComponents<Joint>())
                Destroy(joint);
        }

        public override void SafeAwake()
        {
            healthSlider = AddSlider("Health", "health-value", 0.5F, 0.1F, 3F);
            minimumDamage = AddSlider("Min Impact Damage", "min-damage", 0.5F, 0, 1F);
            fov = AddSlider("Camera FOV", "fov", 65, 50, 100);
            zoomFov = AddSlider("Zoom FOV", "zoom-fov", 20, 10, 100);
            speed = AddSlider("Max Speed", "speed", 10, 0, 150);
            acceleration = AddSlider("Max Acceleration", "acceleration", 75, 0, 200);
            jumpForce = AddSlider("Jump Force", "jump-force", 100, 0, 500);
            sensitivity = AddSlider("Look Sensitivity", "sensitivity", 85F, 0, 200);
            smoothing = AddSlider("Look Smoothing", "smoothing", 60F, 0, 100);
            groundStickDistance = AddSlider("Ground Stick Distance", "stick-distance", 1.5F, 0, 3F);
            pushForceScale = AddSlider("Push Force Scale", "push-force-scale", 1F, 0, 1F);
            interactDistance = AddSlider("Interact Distance", "interact-distance", 5, 0, 15F);
            mass = AddSlider("Mass", "mass", 5, 1, 15F);

            activateKey = AddKey("Active", "active", KeyCode.B);
            jump = AddKey("Jump/Dismount", "jump", KeyCode.LeftAlt);
            interact = AddKey("Interact", "interact", KeyCode.E);
            grab = AddKey("Grab", "grab", KeyCode.F);
            crouch = AddKey("Crouch", "crouch", KeyCode.LeftControl);
            controlled = AddEmulatorKey("Controlled", "controlled", KeyCode.None);

            pitchLimits = AddLimits("Pitch Limits", "pitch", 80, 80, 80, new FauxTransform(new Vector3(-0.5F, 0, 0), Quaternion.Euler(-90, 90, 180), Vector3.one * 0.2F));
            alwaysLabel = AddToggle("Attach Label", "always-label", true);
            visualPitch = AddToggle("Visual Pitch", "visual-pitch", true);
            toggleCrouch = AddToggle("Toggle Crouch", "toggle-crouch", false);
            healthToggle = AddToggle("Has Health", "health-toggle", false);

            pitchLimits.UseLimitsToggle.DisplayInMapper = false;
            
            _oldFov = MainCamera.fieldOfView;
            _oldNearClip = MainCamera.nearClipPlane;

            meshRenderer = GetComponentInChildren<MeshRenderer>();

            healthSlider.DisplayInMapper = false;
            minimumDamage.DisplayInMapper = false;
            healthToggle.Toggled += (value) =>
            {
                minimumDamage.DisplayInMapper = value;
                healthSlider.DisplayInMapper = value;
            };

            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.clip = Mod.Hurt;
            audioSource.spatialBlend = 1F;
            audioSource.playOnAwake = false;
            audioSource.loop = false;

            if (!IsSimulating)
            {
                Collider old = GetComponentInChildren<CapsuleCollider>();
                if (old != null)
                    Destroy(old);

                GameObject topObj = new GameObject("Top");
                topObj.transform.parent = transform;
                topObj.transform.localPosition = Vector3.zero;
                topObj.transform.localEulerAngles = Vector3.zero;
                topObj.transform.localScale = Vector3.one;
                top = topObj.AddComponent<SphereCollider>();
                top.radius = 0.25F;
                top.center = new Vector3(0, 0, 0);
                top.sharedMaterial = Mod.NoFriction;

                GameObject bottomObj = new GameObject("Bottom");
                bottomObj.transform.parent = transform;
                bottomObj.transform.localPosition = Vector3.zero;
                bottomObj.transform.localEulerAngles = Vector3.zero;
                bottomObj.transform.localScale = Vector3.one;
                bottom = bottomObj.AddComponent<CapsuleCollider>();
                bottom.radius = 0.25F;
                bottom.direction = 1;
                bottom.height = 2F;
                bottom.center = new Vector3(0, -0.5F, 0);
                bottom.sharedMaterial = Mod.NoFriction;

                GameObject CreateEffect(GameObject prefab)
                {
                    GameObject obj = ((GameObject)Instantiate(prefab, transform));
                    obj.transform.localPosition = Vector3.zero;
                    return obj;
                }

                bloodBurst = CreateEffect(Mod.BloodBurst).GetComponent<ParticleSystem>();
                bloodBurstHit = CreateEffect(Mod.BloodBurstHit).GetComponent<ParticleSystem>();
                bloodSquirt = CreateEffect(Mod.BloodSquirt).GetComponent<ParticleSystem>();
                bloodQuad = CreateEffect(Mod.BloodQuad).transform;
            }
        }

        public override void OnSimulateStart()
        {
            rotationX = targetRotationX = transform.eulerAngles.y;
            rotationY = targetRotationY = 0;  
            inputRotation = GetLookRotation(rotationX, rotationY).eulerAngles;

            _visualsPosition = BlockBehaviour.MeshRenderer.transform.localPosition;
            _visualsRotation = BlockBehaviour.MeshRenderer.transform.localRotation;

            if (healthToggle.IsActive)
                _previousHealth = Health.health = healthSlider.Value;

            if (Rigidbody != null)
            {
                Rigidbody.constraints = RigidbodyConstraints.FreezeRotation;
                Rigidbody.mass = mass.Value;
                Rigidbody.drag = 0;
            }
        }

        public override void SendKeyEmulationUpdateHost()
        {
            if (clientControlling != _previousControlled)
            {
                EmulateKeys(Mod.NoKeys, controlled, clientControlling);
                _previousControlled = clientControlling;
            }
        }

        public override void SimulateLateUpdateAlways()
        {
            if (alwaysLabel.IsActive)
            {
                SetLabelPosition(transform.position + Vector3.up * (-Machine.InternalObject.Size.y));
            }
        }
        public override void OnSimulateStop()
        {
            if (IsSitting)
                SetSeat(null);

            if (controlling)
                SetControlling(false);
        }

        public override void BuildingLateUpdate()
        {
            if (alwaysLabel.IsActive)
            {
                var centerBlock = Machine.InternalObject.LinkManager.GetLabelTarget();
                if (centerBlock != null)
                    SetLabelPosition(centerBlock.transform.position);
            }
        }

        private void SetLabelPosition(Vector3 position)
        {
            LocalMachine machine = Machine.InternalObject;
            machine.OnAnalysisReset(); // Resets ONLY center preventing machine from overriding controller's value.
            machine.SetMachineCenter(position);
        }

        public override void SimulateUpdateAlways()
        {
            lookingAt = null;   

            if (!visualPitch.IsActive)
            {
                Transform visuals = BlockBehaviour.MeshRenderer.transform;
                if (IsSitting)
                {
                    float angle = (transform.rotation * Quaternion.Inverse(seat.transform.rotation)).eulerAngles.y;

                    visuals.rotation = seat.transform.rotation * ViewOffset * Quaternion.AngleAxis(angle, Vector3.up);
                    visuals.position = seat.transform.position + seat.transform.forward * (seat.crouched.IsActive ? -0.75F : 0.25F);
                } else
                { 
                    visuals.position = transform.position + Quaternion.Inverse(ViewOffset) * _visualsPosition;
                    visuals.rotation = Quaternion.AngleAxis((transform.rotation * ViewOffset).eulerAngles.y, Vector3.up);
                }

                if (Dead)
                {
                    visuals.localPosition = _visualsPosition;
                    visuals.localRotation = _visualsRotation;
                }
            }

            if (IsLocal)
            {
                if (!IsFixedCameraActive && activateKey.IsPressed)
                    SetControlling(!controlling);

                if (IsFixedCameraActive && controlling)
                    SetControlling(false);

                float targetVertical = 0;
                float targetHorizontal = 0;

                if (controlling)
                {
                    if (Input.GetKey(KeyCode.W))
                        targetVertical += 1;
                    if (Input.GetKey(KeyCode.S))
                        targetVertical -= 1;
                    if (Input.GetKey(KeyCode.D))
                        targetHorizontal += 1;
                    if (Input.GetKey(KeyCode.A))
                        targetHorizontal -= 1;
                }

                const float inputMaxDeltaScale = 4;
                vertical = Mathf.MoveTowards(vertical, targetVertical, Time.unscaledDeltaTime * inputMaxDeltaScale);
                horizontal = Mathf.MoveTowards(horizontal, targetHorizontal, Time.unscaledDeltaTime * inputMaxDeltaScale);

                clientControlling = HasAuthority && controlling;

                if (controlling)
                {
                    const float oldVersionScale = 1F / 60F;
                    targetRotationX += Input.GetAxis("Mouse X") * sensitivity.Value * oldVersionScale;
                    targetRotationY += Input.GetAxis("Mouse Y") * sensitivity.Value * oldVersionScale;

                    targetRotationY = ClampAngle(targetRotationY, -85F, 85F);

                    rotationX = Mathf.Lerp(rotationX, targetRotationX, smoothing.Value * Time.unscaledDeltaTime);
                    rotationY = Mathf.Lerp(rotationY, targetRotationY, smoothing.Value * Time.unscaledDeltaTime);

                    _lookRotation = GetLookRotation(rotationX, rotationY);
                }

                if (IsSitting)
                {
                    _lastSittingRotation = _finalRotation = seat.transform.rotation * ViewOffset * _lookRotation;
                }
                else
                    _lastLookRotation = _finalRotation = _lookRotation;

                if (controlling) 
                {
                    if (Dead)
                        MainCamera.transform.rotation = transform.rotation * ViewOffset;
                    else
                        MainCamera.transform.rotation = _finalRotation;

                    Vector3 cameraPosition = GetCameraPosition();

                    MainCamera.transform.position = cameraPosition;
                    MainCamera.fieldOfView = HUDCamera.fieldOfView = zoomFov.Value + (1F - _zoom) * (fov.Value - zoomFov.Value);

                    if (IsSitting)
                        foreach (var key in Seat.EmulatableKeys)
                        {
                            if (HasAuthority)
                            {
                                if (Input.GetKeyDown(key))
                                    SetSeatKey(key, true);
                                if (Input.GetKeyUp(key))
                                    SetSeatKey(key, false);
                            }
                            else
                            {
                                if (Input.GetKeyDown(key))
                                    ModNetworking.SendToHost(Mod.SeatKeyPress.CreateMessage(Block.From(BlockBehaviour), (int)key));
                                if (Input.GetKeyUp(key))
                                    ModNetworking.SendToHost(Mod.SeatKeyRelease.CreateMessage(Block.From(BlockBehaviour), (int)key));
                            }
                        }

                    if (crouch.IsPressed && !IsSitting) // Client relied check is bad.
                        if (toggleCrouch.IsActive)
                            SetTargetCrouching(!targetCrouching);
                        else
                            SetTargetCrouching(true);

                    if (crouch.IsReleased && !toggleCrouch.IsActive)
                        SetTargetCrouching(false);

                    if (jump.IsPressed)
                        if (HasAuthority)
                            Jump();
                        else
                            ModNetworking.SendToHost(Mod.Jump.CreateMessage(Block.From(BlockBehaviour)));

                    _zoom = Mathf.Clamp01(_zoom - Input.mouseScrollDelta.y * Time.deltaTime * 10);

                    // This solution is required to fix unknown bug that can't be reproduced. The fix is to make sure nothing will prevent interaction.

                    if (_hits == null)
                        _hits = new RaycastHit[16];

                    int count = Physics.RaycastNonAlloc(cameraPosition, MainCamera.transform.forward, _hits, interactDistance.Value, Game.BlockEntityLayerMask);
                    for (int i = 0; i < count; i++)
                    {
                        RaycastHit hit = _hits[i];

                        Interactable hitInteractable = hit.transform.GetComponent<Interactable>();
                        if (hitInteractable != null && hitInteractable.IsSimulating)
                        {
                            lookingAt = hitInteractable;
                            break;
                        }
                    }

                    if (lookingAt != null)
                    {
                        if (interact.IsPressed)
                        {
                            interactingWith = lookingAt;
                            StartInteraction(interactingWith);
                        }
                    }

                    if (interact.IsReleased && interactingWith != null)
                    {
                        StopInteraction(interactingWith);
                        interactingWith = null;
                    }

                    if (lookingAt == null && interactingWith != null)
                    {
                        StopInteraction(interactingWith);
                        interactingWith = null;
                    }

                    Cursor.visible = false;
                }

                if (!controlling && interactingWith != null)
                {
                    StopInteraction(interactingWith);
                    interactingWith = null;
                }
            }
        }

        private Vector3 GetCameraPosition()
        {
            Vector3 cameraPosition;

            if (IsSitting)
                cameraPosition = seat.transform.position + seat.transform.forward * seat.EyesHeight;
            else
                cameraPosition = transform.position + transform.forward * 0.25F;

            return cameraPosition;
        }

        public void SetSeatKey(KeyCode key, bool value)
        {
            if (IsSitting && !Dead)
            {
                seat.SetKey(key, value);
            }
        }

        public void TryStartInteraction(Interactable interactable)
        {
            if (interactable.User == null && interactable.IsSimulating && !Dead)
            {
                interactable.StartInteraction(this);
                ModNetworking.SendToAll(Mod.RemoteStartInteraction.CreateMessage(Block.From(BlockBehaviour), Block.From(interactable.BlockBehaviour)));
            }
        }

        public void TryStopInteraction(Interactable interactable)
        {
            if (interactable.User == this && interactable.IsSimulating && !Dead)
            {
                interactable.StopInteraction();
                ModNetworking.SendToAll(Mod.RemoteStopInteraction.CreateMessage(Block.From(BlockBehaviour), Block.From(interactable.BlockBehaviour)));
            }
        }

        public void StartInteraction(Interactable interactable)
        {
            if (HasAuthority)
                TryStartInteraction(interactable);
            else
                ModNetworking.SendToHost(Mod.RequestStartInteraction.CreateMessage(Block.From(BlockBehaviour), Block.From(interactable.BlockBehaviour)));
        }

        public void StopInteraction(Interactable interactable)
        {
            if (HasAuthority)
                TryStopInteraction(interactable);
            else
                ModNetworking.SendToHost(Mod.RequestStopInteraction.CreateMessage(Block.From(BlockBehaviour), Block.From(interactable.BlockBehaviour)));
        }

        private void FixedUpdate()
        {
            if (!Dead)
                bottom.gameObject.transform.eulerAngles = Vector3.zero;

            if (Dead && !_died)
            {
                Death();
            }

            if (healthToggle.IsActive && Health.health != _previousHealth)
            {
                BloodHit();
                _previousHealth = Health.health;
            }

            if (IsLocal)
            {
                if (controlling)
                {
                    MouseOrbit.Instance.camForward = MainCamera.transform.forward;
                    MouseOrbit.Instance.camUp = MainCamera.transform.up;

                    inputDirection = Vector3.ProjectOnPlane(MainCamera.transform.forward, Vector3.up).normalized * vertical + MainCamera.transform.right * horizontal;
                }
                else
                    inputDirection = Vector3.zero; 

                inputRotation = _finalRotation.eulerAngles;

                if (!HasAuthority && _localTicksCount % 5 == 0)
                {
                    Message inputMessage = Mod.SetControllerInput.CreateMessage(Block.From(BlockBehaviour), inputDirection, inputRotation, controlling);
                    ModNetworking.SendToHost(inputMessage);
                }
                _localTicksCount++;
            } 

            if (HasAuthority && IsSimulating)
            {
                if (BlockBehaviour.fireTag.burning)
                    Health.DamageBlock(0.004F);

                if (BlockBehaviour.iceTag.frozen)
                    Health.DamageBlock(0.004F);

                isGrounded = Physics.Raycast(transform.position + Vector3.down + CrouchOffset, Vector2.down, 0.75F, ControllerCollisionMask);

                bool crouchingSit = IsSitting && seat.crouched.IsActive;
                bool shouldCrouch = targetCrouching || crouchingSit;

                if ((shouldCrouch != _crouching) && !Dead)
                {     
                    if (shouldCrouch)
                    {
                        bottom.height = 1F;
                        bottom.center = new Vector3(0, 0F, 0);
                        if (isGrounded)
                            Rigidbody.MovePosition(transform.position - Vector3.up);
                        _crouching = true;
                    }

                    if (!shouldCrouch)
                    {
                        if (!Physics.SphereCast(transform.position, 0.25F, Vector2.up, out RaycastHit hit, 1F, ControllerCollisionMask))
                        {
                            bottom.height = 2F;
                            bottom.center = new Vector3(0, -0.5F, 0);
                            Rigidbody.MovePosition(transform.position + Vector3.up);
                            _crouching = false;
                        }
                    }
                }

                float timestep = Time.fixedDeltaTime;

                Vector3 groundVelocity = GetGroundVelocity();
                Vector3 bodyVelocity = Rigidbody.velocity;

                Vector3 input = Vector3.Scale(Vector3.ClampMagnitude(inputDirection, 1) * (_crouching && isGrounded ? 0.5F : 1F), new Vector3(1, 0, 1));

                if (Physics.CapsuleCast(transform.position + Vector3.up * 0.25F, transform.position + Vector3.down * (_crouching ? 0.25F : 1.25F), 0.2F, input, speed.Value * timestep, ControllerCollisionMask, QueryTriggerInteraction.Ignore))
                    input *= pushForceScale.Value;

                Vector3 goalVel;

                if (IsSitting)
                {
                    groundVelocity = seat.Rigidbody.velocity;
                    const float seatApproachSpeed = 16;
                    Vector3 targetDirection = seat.transform.position + (seat.transform.forward * (crouchingSit ? 0.75F : 1.75F)) - transform.position;
                    goalVel = groundVelocity + Vector3.ClampMagnitude(targetDirection, 1) * seatApproachSpeed;
                    top.enabled = bottom.enabled = targetDirection.magnitude < 0.2F;
                }
                else
                {
                    goalVel = groundVelocity + input * speed.Value;
                    top.enabled = bottom.enabled = true;
                }

                goalVel = Vector3.MoveTowards(goalVel, goalVel + bodyVelocity, timestep);

                Vector3 neededAccel = (goalVel - bodyVelocity) / timestep;

                neededAccel = Vector3.ClampMagnitude(neededAccel, groundVelocity == Vector3.zero ? acceleration.Value : 1000000);

                Quaternion target = GetBodyRotation(inputRotation);

                const float maxRotationDelta = 10F;

                float rotationDelta = Quaternion.Angle(Rigidbody.rotation, target);

                if (!grabbed && grab.IsHeld && !grabJoint && clientControlling &&
                    Physics.Raycast(GetCameraPosition(), transform.rotation * ViewOffset * Vector3.forward, out RaycastHit grabHit, interactDistance.Value, Game.BlockEntityLayerMask, QueryTriggerInteraction.Ignore))
                {
                    var body = grabHit.collider.attachedRigidbody;
                    if (body)
                    {
                        grabbed = body;
                        grabJoint = gameObject.AddComponent<SpringJoint>();
                        grabJoint.connectedBody = grabbed;
                        grabJoint.anchor = new Vector3(0, -grabHit.distance, 0);
                        grabJoint.autoConfigureConnectedAnchor = false;
                        grabJoint.connectedAnchor = grabbed.transform.InverseTransformPoint(grabHit.point);
                        grabJoint.spring = 10000;
                        grabJoint.damper = 1000;
                        grabJoint.enableCollision = true;
                        grabJoint.breakForce = grabJoint.breakTorque = 5000;
                    }
                }

                if (!grab.IsHeld || Dead)
                {

                    if (grabJoint)
                    {
                        Destroy(grabJoint);
                        grabJoint = null;
                    }
                }

                if (grabbed && !grabJoint)
                {
                    grabbed = null;
                }

                if (!Dead)
                {
                    Rigidbody.constraints = RigidbodyConstraints.FreezeRotation;

                    Rigidbody.AddForce(Vector3.Scale(neededAccel, new Vector3(1, IsSitting ? 1 : 0, 1)) * Rigidbody.mass);

                    if (rotationDelta < maxRotationDelta)
                        Rigidbody.rotation = Quaternion.Lerp(Rigidbody.rotation, target, 0.5F);
                    else
                        Rigidbody.rotation = Quaternion.RotateTowards(Rigidbody.rotation, target, maxRotationDelta);
                }
            }
        }

        public void Death()
        {
            if (!IsSimulating)
                return;

            _died = true;
            Health.health = 0;

            top.sharedMaterial = Mod.Limb;
            bottom.sharedMaterial = Mod.Limb;

            SetSeat(null);

            if (Rigidbody != null)
            {
                Rigidbody.constraints = RigidbodyConstraints.None;
                Rigidbody.mass = 0.3F;
            }

            if (OptionsMaster.BesiegeConfig.BloodEnabled)
            {
                BlockBehaviour.Prefab.bleedOnTexture = true;
                BlockBehaviour.BloodSplatter();
                audioSource.Play();
                BloodParticles();
                BloodQuad();
            }

            bottom.gameObject.transform.localRotation = ViewOffset;

            if (HasAuthority)
                ModNetworking.SendToAll(Mod.Death.CreateMessage(Block.From(BlockBehaviour)));
        }

        public void BloodHit()
        {
            if (!IsSimulating)
                return;

            if (HasAuthority)
                ModNetworking.SendToAll(Mod.BloodHit.CreateMessage(Block.From(BlockBehaviour)));

            if (!OptionsMaster.BesiegeConfig.BloodEnabled)
                return;

            if (bloodBurstHit && !bloodBurstHit.isPlaying)
            {
                bloodBurstHit.startColor = StatMaster.BloodColor;
                bloodBurstHit.GetComponent<ParticleSystemRenderer>().material.SetColor("_TintColor", StatMaster.BloodColor);
                bloodBurstHit.Play();
            }
        }
        
        public void BloodParticles()
        {
            if (!IsSimulating)
                return;

            if (HasAuthority)
                ModNetworking.SendToAll(Mod.BloodParticles.CreateMessage(Block.From(BlockBehaviour)));

            if (!OptionsMaster.BesiegeConfig.BloodEnabled)
                return;

            Color bloodColor = StatMaster.BloodColor;
            bloodBurst.startColor = bloodColor;
            bloodSquirt.startColor = bloodColor;
            bloodSquirt.GetComponent<ParticleSystemRenderer>().material.SetColor("_TintColor", StatMaster.BloodColor);
            bloodBurst.GetComponent<ParticleSystemRenderer>().material.SetColor("_TintColor", StatMaster.BloodColor);
            if (!bloodSquirt.isPlaying)
                bloodSquirt.Play();
            if (!bloodBurst.isPlaying)
                bloodBurst.Play();
        }

        public override void OnSimulateCollisionEnter(Collision collision)
        {
            float magnitude = collision.relativeVelocity.magnitude;
            float massA = Rigidbody.mass;
            float damage = massA * magnitude * magnitude / 10000F;

            if (damage > minimumDamage.Value)
            {
                Health.DamageBlock(damage);
                if (Dead)
                {
                    if (Rigidbody != null)
                    {
                        Rigidbody.constraints = RigidbodyConstraints.None;
                        Rigidbody.mass = 0.3F;
                    }
                }
            }

            if (Dead)
            {
                if (OptionsMaster.BesiegeConfig.BloodEnabled)
                {
                    BlockBehaviour behaviour = collision.rigidbody?.GetComponent<BlockBehaviour>();
                    if (behaviour != null)
                        behaviour.BloodSplatter();

                    BloodParticles();
                }
            }
        }

        public void BloodQuad()
        {
            if (!OptionsMaster.BesiegeConfig.BloodEnabled || bloodQuad == null)
                return;
            if (transform.position.y - SingleInstanceFindOnly<AddPiece>.Instance.floorHeight > 3F)
                return;
            MeshRenderer bloodQuadRenderer = bloodQuad.GetComponent<MeshRenderer>();
            bloodQuadRenderer.material.color = StatMaster.BloodColor;
            bloodQuadRenderer.enabled = true;
            bloodQuad.parent = ReferenceMaster.physicsGoalInstance;
            bloodQuad.position = new Vector3(transform.position.x, SingleInstanceFindOnly<AddPiece>.Instance.floorHeight + 0.05f, transform.position.z);
            bloodQuad.forward = -Vector3.up;
            bloodQuad.localEulerAngles = new Vector3(bloodQuad.localEulerAngles.x, bloodQuad.localEulerAngles.y, UnityEngine.Random.Range(0f, 360f));
            bloodQuad.GetComponent<Decal>().enabled = true;
        }

        private Quaternion GetBodyRotation(Vector3 inputRotation)
        {
            Vector3 rotation = inputRotation;

            if (rotation.x > 180)
                rotation.x -= 360;
            if (rotation.z > 180)
                rotation.z -= 360;

            Quaternion target = Quaternion.Euler(
                Mathf.Clamp(rotation.x, -pitchLimits.Max, pitchLimits.Min),
                rotation.y,
                0)
                * Quaternion.AngleAxis(90, -Vector3.right)
                * Quaternion.AngleAxis(rotation.z, -Vector3.up); // Working quaternion nonsense.

            return target;
        }

        public void SetControlling(bool value)
        {
            if (Current != null && Current != this)
                return;

            Current = this;

            controlling = value;

            Cursor.visible = !value;
            Cursor.lockState = value ? CursorLockMode.Locked : CursorLockMode.None;

            MouseOrbit.Instance.isActive = !value;

            MainCamera.fieldOfView = HUDCamera.fieldOfView = value ? fov.Value : _oldFov;
            MainCamera.nearClipPlane = HUDCamera.nearClipPlane = value ? 0.05F : _oldNearClip;

            meshRenderer.shadowCastingMode = 
                value ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly : UnityEngine.Rendering.ShadowCastingMode.On;

            if (!controlling)
            {
                Current = null;

                SetTargetCrouching(false);

                if (IsSitting)
                    foreach (var key in Seat.EmulatableKeys)
                    {
                        if (HasAuthority)
                            SetSeatKey(key, false);
                        else
                            ModNetworking.SendToHost(Mod.SeatKeyRelease.CreateMessage(Block.From(BlockBehaviour), (int)key));
                    }
            }
        }

        public void SetTargetCrouching(bool value)
        {
            targetCrouching = value; // Move this set to msg recieve.
            if (!HasAuthority)
                ModNetworking.SendToHost(Mod.SetCrouch.CreateMessage(Block.From(BlockBehaviour), value));
        }

        public void SetSeat(Seat seat)
        {
            if (seat == null)
                rotationX = targetRotationX = _lastSittingRotation.eulerAngles.y;
            else
            {
                Quaternion pitchOffset = Quaternion.AngleAxis(-90, -Vector3.right); // TODO: Repeated code.
                rotationX = targetRotationX = (_lastLookRotation * Quaternion.Inverse(seat.transform.rotation * pitchOffset)).eulerAngles.y;
            }

            this.seat?.SetFree();
            this.seat = seat;

            SetTargetCrouching(false);

            if (HasAuthority)
            {
                Rigidbody.useGravity = !IsSitting;
            }
        }

        private void OnGUI()
        {
            if (controlling)
            {
                GUI.DrawTexture(new Rect(Screen.width / 2 - Mod.Crosshair.width / 2, Screen.height / 2 - Mod.Crosshair.height, Mod.Crosshair.width, Mod.Crosshair.height), Mod.Crosshair);
                
                if (lookingAt != null && lookingAt.label.Value.Length > 0)
                {
                    float left = Screen.width / 2 - 75;
                    float top = Screen.height / 2 + 100;
                    GUI.DrawTexture(new Rect(left, top, 150, 40), Mod.HintBackground);
                    GUI.Label(new Rect(left + 1, top + 1, 150, 40), lookingAt.hint, Mod.InfoTextShadow);
                    GUI.Label(new Rect(left, top, 150, 40), lookingAt.hint, Mod.InfoText);
                }
            }
        }

        public static float ClampAngle(float angle, float min, float max)
        {
            if (angle < -360F)
                angle += 360F;
            if (angle > 360F)
                angle -= 360F;
            return Mathf.Clamp(angle, min, max);
        }

        public void Jump()
        {
            if (HasAuthority)
            {
                if (!IsSitting && !Dead)
                    if (isGrounded)
                        Rigidbody.AddForce(Vector3.up * jumpForce.Value, ForceMode.Impulse);

                ModNetworking.SendToAll(Mod.Jump.CreateMessage(Block.From(BlockBehaviour)));
            }

            if (IsSitting)
                SetSeat(null);
        }

        private RaycastHit[] _overlap = new RaycastHit[8];

        public Vector3 GetGroundVelocity()
        {
            int count = Physics.SphereCastNonAlloc(transform.position + Vector3.down * 1.5F + CrouchOffset, 0.24F, Vector3.down, _overlap, groundStickDistance.Value, Game.BlockEntityLayerMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _overlap[i];
                if (hit.rigidbody != null && hit.rigidbody.GetComponent<Controller>() == null)
                    return hit.rigidbody.velocity;
            }
            return Vector3.zero;
        }

        public static Quaternion GetLookRotation(float x, float y)
        {
            Quaternion xQuaternion = Quaternion.AngleAxis(x, Vector3.up);
            Quaternion yQuaternion = Quaternion.AngleAxis(y, -Vector3.right);
            return xQuaternion * yQuaternion;
        }

        public bool OnExplode(float power, float upPower, float torquePower, Vector3 explosionPos, float radius, int mask)
        {
            float damage = power * Mathf.Clamp01(1F - Vector3.Distance(transform.position, explosionPos) / radius);
            damage /= 1000F;

            if (HasAuthority)
                Health.DamageBlock(damage);

            if (Rigidbody != null && Dead)  
            {
                Rigidbody.mass = 0.3F;
                Rigidbody.AddExplosionForce(power / 200F, explosionPos, radius, upPower / 200F, ForceMode.Impulse);
            }
            return true;
        }
    }
}
