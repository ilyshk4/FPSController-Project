using System;
using System.Collections.Generic;
using System.Linq;
using Modding;
using Modding.Blocks;
using UnityEngine;
using LocalMachine = Machine;

namespace FPSController
{
    public class Controller : BlockScript
    {
        public MeshRenderer meshRenderer;

        public Vector3 inputDirection = Vector3.forward;
        public Vector3 inputRotation = Vector3.zero;

        public float rotationX, rotationY;
        public float targetRotationX, targetRotationY;

        public MSlider fov;
        public MSlider speed;
        public MSlider acceleration;
        public MSlider sensitivity;
        public MSlider smoothing;
        public MSlider groundStickDistance;
        public MSlider groundStickSpread;
        public MSlider interactDistance;
        public MSlider jumpForce;
        public MSlider mass;

        public MKey activateKey;
        public MKey jump;
        public MKey interact;
        public MKey crouch;

        public MLimits pitchLimits;

        public MToggle alwaysLabel;
        public MToggle visualPitch;
        public MToggle toggleCrouch;

        public Interactable lookingAt;
        public Interactable interactingWith;

        public SphereCollider top;
        public CapsuleCollider bottom;

        public Seat seat;

        public bool controlling;
        public bool isGrounded;
        public bool targetCrouching;

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

        private float _oldFov;
        private float _oldNearClip;
        private long _localTicksCount = 0;
        private Quaternion _finalRotation;
        private Quaternion _lastSittingRotation;
        private Quaternion _lookRotation;
        private Quaternion _lastLookRotation;
        private Vector3 _visualsPosition;

        private bool _crouching;

        private static Quaternion ViewOffset = Quaternion.AngleAxis(-90, -Vector3.right);

        private static int ControllerCollisionMask = Game.BlockEntityLayerMask | 1 | 1 << 29;

        private void Start()
        {
            foreach (var joint in GetComponents<Joint>())
                Destroy(joint);
        }

        public override void SafeAwake()
        {
            fov = AddSlider("Camera FOV", "fov", 65, 50, 100);
            speed = AddSlider("Max Speed", "speed", 10, 0, 150);
            acceleration = AddSlider("Max Acceleration", "acceleration", 75, 0, 200);
            jumpForce = AddSlider("Jump Force", "jump-force", 100, 0, 500);
            sensitivity = AddSlider("Look Sensitivity", "sensitivity", 85F, 0, 200);
            smoothing = AddSlider("Look Smoothing", "smoothing", 60F, 0, 100);
            groundStickDistance = AddSlider("Ground Stick Distance", "stick-distance", 1.5F, 0, 3F);
            groundStickSpread = AddSlider("Ground Stick Spread", "stick-spread", 20, 0, 85);
            interactDistance = AddSlider("Interact Distance", "interact-distance", 5, 0, 15F);
            mass = AddSlider("Mass", "mass", 5, 1, 15F);
            activateKey = AddKey("Active", "active", KeyCode.B);
            jump = AddKey("Jump", "jump", KeyCode.LeftAlt);
            interact = AddKey("Interact", "interact", KeyCode.E);
            crouch = AddKey("Crouch", "crouch", KeyCode.LeftControl);
            pitchLimits = AddLimits("Pitch Limits", "pitch", 80, 80, 80, new FauxTransform(new Vector3(-0.5F, 0, 0), Quaternion.Euler(-90, 90, 180), Vector3.one * 0.2F));
            alwaysLabel = AddToggle("Attach Label", "always-label", true);
            visualPitch = AddToggle("Visual Pitch", "visual-pitch", true);
            toggleCrouch = AddToggle("Toggle Crouch", "toggle-crouch", false);
            pitchLimits.UseLimitsToggle.DisplayInMapper = false;
            
            _oldFov = MainCamera.fieldOfView;
            _oldNearClip = MainCamera.nearClipPlane;

            meshRenderer = GetComponentInChildren<MeshRenderer>();

            if (!IsSimulating)
            {
                Collider old = GetComponentInChildren<CapsuleCollider>();
                if (old != null)
                    Destroy(old);

                GameObject topObj = new GameObject("Top");
                topObj.transform.parent = transform;
                topObj.transform.localPosition = Vector3.zero;
                topObj.transform.localEulerAngles = Vector3.zero;
                top = topObj.AddComponent<SphereCollider>();
                top.radius = 0.25F;
                top.center = new Vector3(0, 0, 0);
                top.sharedMaterial = Mod.NoFriction;

                GameObject bottomObj = new GameObject("Bottom");
                bottomObj.transform.parent = transform;
                bottomObj.transform.localPosition = Vector3.zero;
                bottomObj.transform.localEulerAngles = Vector3.zero;
                bottom = bottomObj.AddComponent<CapsuleCollider>();
                bottom.radius = 0.25F;
                bottom.direction = 1;
                bottom.height = 2F;
                bottom.center = new Vector3(0, -0.5F, 0);
                bottom.sharedMaterial = Mod.NoFriction;
            }
        }

        public override void OnSimulateStart()
        {
            rotationX = targetRotationX = transform.eulerAngles.y;
            rotationY = targetRotationY = 0;  
            inputRotation = GetLookRotation(rotationX, rotationY).eulerAngles;

            _visualsPosition = BlockBehaviour.MeshRenderer.transform.localPosition;
        }

        public override void SimulateLateUpdateAlways()
        {
            if (alwaysLabel.IsActive)
            {
                LocalMachine machine = Machine.InternalObject;
                machine.OnAnalysisReset(); // Resets ONLY center preventing machine from overriding controller's value.
                machine.SetMachineCenter(transform.position + Vector3.up * (-machine.Size.y));
            }
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
                    visuals.position = seat.transform.position + seat.transform.forward * 0.25F;
                } else
                {
                    visuals.position = transform.position + Quaternion.Inverse(ViewOffset) * _visualsPosition;
                    visuals.rotation = Quaternion.AngleAxis((transform.rotation * ViewOffset).eulerAngles.y, Vector3.up);
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
                    MainCamera.transform.rotation = _finalRotation;

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

                    if (crouch.IsPressed)
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

                    if (Physics.Raycast(MainCamera.transform.position, MainCamera.transform.forward, out RaycastHit hit, interactDistance.Value, Game.BlockEntityLayerMask))
                    {
                        Interactable hitInteractable = hit.transform.GetComponent<Interactable>();
                        if (hitInteractable != null && hitInteractable.IsSimulating)
                            lookingAt = hitInteractable;
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

                    Vector3 cameraPosition;

                    if (IsSitting)
                        cameraPosition = seat.transform.position + seat.transform.forward * seat.EyesHeight;
                    else
                        cameraPosition = transform.position + transform.forward * 0.25F;

                    MainCamera.transform.position = cameraPosition;

                    Cursor.visible = false;
                }

                if (!controlling && interactingWith != null)
                {
                    StopInteraction(interactingWith);
                    interactingWith = null;
                }
            }
        }

        public void SetSeatKey(KeyCode key, bool value)
        {
            if (IsSitting)
            {
                seat.SetKey(key, value);
            }
        }

        public void TryStartInteraction(Interactable interactable)
        {
            if (interactable.User == null && interactable.IsSimulating)
            {
                interactable.StartInteraction(this);
                ModNetworking.SendToAll(Mod.RemoteStartInteraction.CreateMessage(Block.From(BlockBehaviour), Block.From(interactable.BlockBehaviour)));
            }
        }

        public void TryStopInteraction(Interactable interactable)
        {
            if (interactable.User == this && interactable.IsSimulating)
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
            bottom.gameObject.transform.eulerAngles = Vector3.zero;

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
                    Message inputMessage = Mod.SetControllerInput.CreateMessage(Block.From(BlockBehaviour), inputDirection, inputRotation);
                    ModNetworking.SendToHost(inputMessage);
                }
                _localTicksCount++;
            }

            if (HasAuthority && IsSimulating)
            {
                Rigidbody.constraints = RigidbodyConstraints.FreezeRotation;
                Rigidbody.angularDrag = 16;
                Rigidbody.drag = 0;
                Rigidbody.mass = mass.Value;

                isGrounded = Physics.Raycast(transform.position, Vector2.down, 1.75F, ControllerCollisionMask);

                if (targetCrouching != _crouching)
                {     
                    if (targetCrouching)
                    {
                        bottom.height = 1F;
                        bottom.center = new Vector3(0, 0F, 0);
                        // Rigidbody.MovePosition(transform.position - Vector3.up);
                        _crouching = true;
                    }

                    if (!targetCrouching)
                    {
                        if (!Physics.CheckSphere(transform.position + Vector3.up * 1F, 0.4F, ControllerCollisionMask))
                        {
                            bottom.height = 2F;
                            bottom.center = new Vector3(0, -0.5F, 0);
                            Rigidbody.MovePosition(transform.position + Vector3.up);
                            _crouching = false;
                        }
                    }
                }

                float timestep = Time.fixedDeltaTime;

                Vector3 groundVelocity = GetGroundVelocitySpread();
                Vector3 bodyVelocity = Rigidbody.velocity;

                inputDirection = Vector3.ClampMagnitude(inputDirection, 1) * (_crouching && isGrounded ? 0.5F : 1F);

                Vector3 goalVel;

                if (IsSitting)
                {
                    groundVelocity = seat.Rigidbody.velocity;
                    const float seatApproachSpeed = 16;
                    goalVel = groundVelocity + Vector3.ClampMagnitude(seat.transform.position + seat.transform.forward * 1.75F - transform.position, 1) * seatApproachSpeed;
                }
                else
                {
                    goalVel = groundVelocity + Vector3.Scale(inputDirection, new Vector3(1, 0, 1)) * speed.Value;
                }

                goalVel = Vector3.MoveTowards(goalVel, goalVel + bodyVelocity, timestep);

                Vector3 neededAccel = (goalVel - bodyVelocity) / timestep;

                neededAccel = Vector3.ClampMagnitude(neededAccel, groundVelocity == Vector3.zero ? acceleration.Value : 1000000);

                Rigidbody.AddForce(Vector3.Scale(neededAccel, new Vector3(1, IsSitting ? 1 : 0, 1)) * Rigidbody.mass);

                Quaternion target = GetBodyRotation(inputRotation);

                const float maxRotationDelta = 10F;

                float rotationDelta = Quaternion.Angle(Rigidbody.rotation, target);

                if (rotationDelta < maxRotationDelta)
                    Rigidbody.rotation = Quaternion.Lerp(Rigidbody.rotation, target, 0.5F);
                else
                    Rigidbody.rotation = Quaternion.RotateTowards(Rigidbody.rotation, target, maxRotationDelta);
            }
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
            if (HasAuthority)
                targetCrouching = value;
            else
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
                top.enabled = bottom.enabled = !IsSitting;
                Rigidbody.useGravity = !IsSitting;
            }
        }

        public override void OnSimulateStop()
        {
            if (IsSitting)
                SetSeat(null);

            if (controlling)
                SetControlling(false);
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
                if (!IsSitting)
                    if (isGrounded)
                        Rigidbody.AddForce(Vector3.up * jumpForce.Value, ForceMode.Impulse);

                ModNetworking.SendToAll(Mod.Jump.CreateMessage(Block.From(BlockBehaviour)));
            }

            if (IsSitting)
                SetSeat(null);
        }


        public Vector3 GetGroundVelocitySpread()
        {
            float downScale = Mathf.Min(Mathf.Tan(groundStickSpread.Value), 100);
            Vector3 velocity = Vector3.zero;
            velocity = GetGroundVelocity(Vector3.down * downScale);
            if (velocity != Vector3.zero)
                return velocity;
            velocity = GetGroundVelocity(Vector3.down * downScale + Vector3.right);
            if (velocity != Vector3.zero)         
                return velocity;                  
            velocity = GetGroundVelocity(Vector3.down * downScale + Vector3.left);
            if (velocity != Vector3.zero)             
                return velocity;                      
            velocity = GetGroundVelocity(Vector3.down * downScale + Vector3.forward);
            if (velocity != Vector3.zero)        
                return velocity;                 
            velocity = GetGroundVelocity(Vector3.down * downScale + Vector3.back);
            if (velocity != Vector3.zero)            
                return velocity;
            velocity = GetGroundVelocity(Vector3.down * downScale * 0.5F + Vector3.right);
            if (velocity != Vector3.zero)
                return velocity;
            velocity = GetGroundVelocity(Vector3.down * downScale * 0.5F + Vector3.left);
            if (velocity != Vector3.zero)
                return velocity;
            velocity = GetGroundVelocity(Vector3.down * downScale * 0.5F + Vector3.forward);
            if (velocity != Vector3.zero)
                return velocity;
            velocity = GetGroundVelocity(Vector3.down * downScale * 0.5F + Vector3.back);
            if (velocity != Vector3.zero)
                return velocity;
            return velocity;
        }

        public Vector3 GetGroundVelocity(Vector3 direction)
        {
            if (Physics.Raycast(transform.position + Vector3.down * 1.4F, direction, out RaycastHit hit, 1F + groundStickDistance.Value, Game.BlockEntityLayerMask))
                if (hit.rigidbody != null && hit.rigidbody.GetComponent<Controller>() == null)
                    return hit.rigidbody.velocity;
            return Vector3.zero;
        }

        public static Quaternion GetLookRotation(float x, float y)
        {
            Quaternion xQuaternion = Quaternion.AngleAxis(x, Vector3.up);
            Quaternion yQuaternion = Quaternion.AngleAxis(y, -Vector3.right);
            return xQuaternion * yQuaternion;
        }
    }
}
