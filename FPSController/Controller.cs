using System;
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
        public float t_rotationX, t_rotationY;

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

        public MLimits pitchLimits;

        public Interactable lookingAt;
        public Interactable interactingWith;

        public GameObject top, bottom;

        public float vertical, horizontal;

        public bool controlling;
        public bool IsLocal => Machine.InternalObject == LocalMachine.Active() || !StatMaster.isMP;
        public bool IsServer => StatMaster.isHosting || !StatMaster.isMP || StatMaster.isLocalSim;
        public bool IsFixedCameraActive => FixedCameraController.Instance.activeCamera;

        private Camera _viewCamera;
        public Camera ViewCamera
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
        private Vector3 _lastNonZeroRotation = Vector3.forward;
        private Vector3 _originalRotation;
        private long _send = 0;

        private void Start()
        {
            foreach (var joint in GetComponents<Joint>())
                Destroy(joint);
            _originalRotation = transform.eulerAngles;
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
            pitchLimits = AddLimits("Pitch Limits", "pitch", 80, 80, 80, new FauxTransform(new Vector3(-0.5F, 0, 0), Quaternion.Euler(-90, 90, 180), Vector3.one * 0.2F));
            
            _oldFov = ViewCamera.fieldOfView;
            _oldNearClip = ViewCamera.nearClipPlane;

            meshRenderer = GetComponentInChildren<MeshRenderer>();

            if (!IsSimulating)
            {
                Collider old = GetComponentInChildren<CapsuleCollider>();
                if (old != null)
                    Destroy(old);

                top = new GameObject("Top");
                top.transform.parent = transform;
                top.transform.localPosition = Vector3.zero;
                top.transform.localEulerAngles = Vector3.zero;
                SphereCollider bottomCollider = top.AddComponent<SphereCollider>();
                bottomCollider.radius = 0.5F;
                bottomCollider.center = new Vector3(0, 0, 0);
                bottomCollider.sharedMaterial = Mod.LowFriction;

                bottom = new GameObject("Bottom");
                bottom.transform.parent = transform;
                bottom.transform.localPosition = Vector3.zero;
                bottom.transform.localEulerAngles = Vector3.zero;
                SphereCollider topCollider = bottom.AddComponent<SphereCollider>();
                topCollider.radius = 0.5F;
                topCollider.center = new Vector3(0, -1F, 0);
                topCollider.sharedMaterial = Mod.LowFriction;
            }
        }

        public override void OnSimulateStart()
        {
            rotationX = t_rotationX = transform.eulerAngles.y;
            rotationY = t_rotationY = 0;
            Quaternion xQuaternion = Quaternion.AngleAxis(rotationX, Vector3.up);
            Quaternion yQuaternion = Quaternion.AngleAxis(rotationY, -Vector3.right);
            _lastNonZeroRotation = inputRotation = (xQuaternion * yQuaternion).eulerAngles;
        }

        public override void SimulateUpdateAlways()
        {
            lookingAt = null;
            
            if (IsLocal)
            {
                if (!IsFixedCameraActive && activateKey.IsPressed)
                    SetControlling(!controlling);

                if (IsFixedCameraActive && controlling)
                    SetControlling(false);

                const float inputStepScale = 4;

                float t_vertical = 0;
                float t_horizontal = 0;

                if (controlling)
                {
                    if (Input.GetKey(KeyCode.W))
                        t_vertical += 1;
                    if (Input.GetKey(KeyCode.S))
                        t_vertical -= 1;
                    if (Input.GetKey(KeyCode.D))
                        t_horizontal += 1;
                    if (Input.GetKey(KeyCode.A))
                        t_horizontal -= 1;
                }

                vertical = Mathf.MoveTowards(vertical, t_vertical, Time.unscaledDeltaTime * inputStepScale);
                horizontal = Mathf.MoveTowards(horizontal, t_horizontal, Time.unscaledDeltaTime * inputStepScale);

                if (controlling)
                {
                    t_rotationX += Input.GetAxis("Mouse X") * sensitivity.Value * 0.016667F;
                    t_rotationY += Input.GetAxis("Mouse Y") * sensitivity.Value * 0.016667F;
                    t_rotationY = ClampAngle(t_rotationY, -85F, 85F);
                    rotationX = Mathf.Lerp(rotationX, t_rotationX, smoothing.Value * Time.unscaledDeltaTime);
                    rotationY = Mathf.Lerp(rotationY, t_rotationY, smoothing.Value * Time.unscaledDeltaTime);
                    Quaternion xQuaternion = Quaternion.AngleAxis(rotationX, Vector3.up);
                    Quaternion yQuaternion = Quaternion.AngleAxis(rotationY, -Vector3.right);
                    ViewCamera.transform.localRotation = xQuaternion * yQuaternion;

                    if (jump.IsPressed)
                        if (IsServer)
                            Jump();
                        else
                            ModNetworking.SendToHost(Mod.Jump.CreateMessage(Block.From(BlockBehaviour)));

                    if (Physics.Raycast(ViewCamera.transform.position, ViewCamera.transform.forward, out RaycastHit hit, interactDistance.Value, Game.BlockEntityLayerMask))
                    {
                        Interactable hitInteractable = hit.transform.GetComponent<Interactable>();
                        if (hitInteractable != null)
                            lookingAt = hitInteractable;
                        else
                            lookingAt = null;
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

                    ViewCamera.transform.position = transform.position + transform.forward * 0.25F;
                    Cursor.visible = false;
                }

                if (!controlling && interactingWith != null)
                {
                    StopInteraction(interactingWith);
                    interactingWith = null;
                }
            }
        }

        public void StartInteraction(Interactable interactable)
        {
            interactable.StartInteraction(this);
            ModNetworking.SendToAll(Mod.StartInteraction.CreateMessage(Block.From(BlockBehaviour), Block.From(interactable.BlockBehaviour)));
        }

        public void StopInteraction(Interactable interactable)
        {
            interactable.StopInteraction(this);
            ModNetworking.SendToAll(Mod.StopInteraction.CreateMessage(Block.From(BlockBehaviour), Block.From(interactable.BlockBehaviour)));
        }

        private void FixedUpdate()
        {
            bottom.transform.eulerAngles = Vector3.zero;

            if (inputRotation != Vector3.zero)
                _lastNonZeroRotation = inputRotation;
             
            if (IsLocal)
            {
                if (controlling)
                {
                    MouseOrbit.Instance.camForward = ViewCamera.transform.forward;
                    MouseOrbit.Instance.camUp = ViewCamera.transform.up;

                    inputDirection = Vector3.ProjectOnPlane(ViewCamera.transform.forward, Vector3.up).normalized * vertical + ViewCamera.transform.right * horizontal;
                    inputRotation = ViewCamera.transform.localEulerAngles;
                }
                else
                    inputDirection = inputRotation = Vector3.zero;
                _send++;
                if (!IsServer && _send % 5 == 0)
                {
                    Message directionMessage = Mod.SetControllerDirectionRotation.CreateMessage(Block.From(BlockBehaviour), inputDirection, inputRotation);
                    ModNetworking.SendToHost(directionMessage);
                } 
            }

            if (IsServer && IsSimulating)
            {
                Rigidbody.constraints = RigidbodyConstraints.FreezeRotation;
                Rigidbody.angularDrag = 10;
                Rigidbody.drag = 0;
                Rigidbody.mass = mass.Value;

                float timestep = Time.fixedDeltaTime;

                Vector3 groundVelocity = GetGroundVelocitySpread();
                Vector3 bodyVelocity = Rigidbody.velocity;

                inputDirection = Vector3.ClampMagnitude(inputDirection, 1);
                Vector3 goalVel = groundVelocity + Vector3.Scale(inputDirection, new Vector3(1, 0, 1)) * speed.Value;

                goalVel = Vector3.MoveTowards(goalVel, goalVel + bodyVelocity, timestep);

                Vector3 neededAccel = (goalVel - bodyVelocity) / timestep;

                neededAccel = Vector3.ClampMagnitude(neededAccel, groundVelocity == Vector3.zero ? acceleration.Value : 1000000);

                Rigidbody.AddForce(Vector3.Scale(neededAccel, new Vector3(1, 0, 1)) * Rigidbody.mass);

                if (_lastNonZeroRotation.x > 180)
                    _lastNonZeroRotation.x = _lastNonZeroRotation.x - 360;

                const float maxRotationDelta = 30;

                Quaternion target = Quaternion.Euler(_originalRotation.x + Mathf.Clamp(_lastNonZeroRotation.x, -pitchLimits.Max, pitchLimits.Min), _lastNonZeroRotation.y, _originalRotation.z);

                Rigidbody.rotation = Quaternion.RotateTowards(Rigidbody.rotation, target, maxRotationDelta);
            }
        }

        public void SetControlling(bool value)
        {
            controlling = value;

            Cursor.visible = !value;
            Cursor.lockState = value ? CursorLockMode.Locked : CursorLockMode.None;
            ViewCamera.fieldOfView = HUDCamera.fieldOfView = value ? fov.Value : _oldFov;
            ViewCamera.nearClipPlane = HUDCamera.nearClipPlane = value ? 0.05F : _oldNearClip;
            MouseOrbit.Instance.isActive = !value;
            meshRenderer.shadowCastingMode = value ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly : UnityEngine.Rendering.ShadowCastingMode.On;
        }

        public override void OnSimulateStop()
        {
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
            int layerMask = Game.BlockEntityLayerMask | 1 | 1 << 29;

            if (Physics.Raycast(transform.position + Vector3.down, Vector2.down, out RaycastHit hit, 2F, layerMask))
                Rigidbody.AddForce(Vector3.up * jumpForce.Value, ForceMode.Impulse);
        }

        public Vector3 GetGroundVelocitySpread()
        {
            float downScale = Mathf.Min(Mathf.Tan(groundStickSpread.Value), 100);
            Vector3 velocity = Vector3.zero;
            velocity = GetGroundVelocity(Vector3.down);
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
            if (Physics.Raycast(transform.position + Vector3.down, direction, out RaycastHit hit, 1F + groundStickDistance.Value, Game.BlockEntityLayerMask))
                if (hit.rigidbody != null && hit.rigidbody.GetComponent<Controller>() == null)
                    return hit.rigidbody.velocity;
            return Vector3.zero;
        }
    }
}
