using System;
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
        public MSlider interactDistance;
        public MSlider jumpForce;

        public MKey startStopControlling;
        public MKey jump;
        public MKey interact;

        public Interactable lookingAt;
        public Interactable interactingWith;

        public bool controlling;
        public bool IsLocal => Machine.InternalObject == LocalMachine.Active() || !StatMaster.isMP;
        public bool IsServer => StatMaster.isHosting || !StatMaster.isMP;
        public Camera ViewCamera => MouseOrbit.Instance.cam;

        private float _oldFov;
        private Vector3 _lastNonZeroDirection = Vector3.forward;
        private Vector3 _lastNonZeroRotation = Vector3.forward;
        private Vector3 _originalRotation;

        private void Start()
        {
            foreach (var joint in GetComponents<Joint>())
                Destroy(joint);
            _originalRotation = transform.eulerAngles;
        }

        public override void SafeAwake()
        {
            fov = AddSlider("Camera FOV", "controller-fov", 65, 50, 100);
            speed = AddSlider("Max Speed", "controller-speed", 10, 0, 150);
            acceleration = AddSlider("Max Acceleration", "controller-acceleration", 75, 0, 200);
            jumpForce = AddSlider("Jump Force", "controller-jumpForce", 100, 0, 500);
            sensitivity = AddSlider("Look Sensitivity", "controller-sensitivity", 85F, 0, 200);
            smoothing = AddSlider("Look Smoothing", "controller-smoothing", 60F, 0, 100);
            groundStickDistance = AddSlider("Ground Stick Distance", "controller-stick", 1.5F, 0, 3F);
            interactDistance = AddSlider("Interact Distance", "controller-interact-dist", 5, 0, 15F);
            startStopControlling = AddKey("Active", "controller-startstop", KeyCode.B);
            jump = AddKey("Jump", "controller-jump", KeyCode.LeftAlt);
            interact = AddKey("Interact", "controller-interact", KeyCode.E);

            _oldFov = ViewCamera.fieldOfView;

            meshRenderer = GetComponentInChildren<MeshRenderer>();
        }

        public override void SimulateUpdateAlways()
        {
            Debug.Log(IsLocal);
            Debug.Log(Machine.InternalObject);
            Debug.Log(LocalMachine.Active());
            lookingAt = null;
            
            if (IsLocal)
            {
                if (startStopControlling.IsPressed)
                    SetControlling(!controlling);

                if (controlling)
                {
                    t_rotationX += Input.GetAxis("Mouse X") * sensitivity.Value * Time.deltaTime;
                    t_rotationY += Input.GetAxis("Mouse Y") * sensitivity.Value * Time.deltaTime;
                    t_rotationY = ClampAngle(t_rotationY, -85F, 85F);
                    rotationX = Mathf.LerpAngle(rotationX, t_rotationX, smoothing.Value * Time.deltaTime);
                    rotationY = Mathf.LerpAngle(rotationY, t_rotationY, smoothing.Value * Time.deltaTime);
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

                    ViewCamera.transform.position = transform.position + new Vector3(0, 1.75F, 0);
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
            interactable.StartInteraction();
            ModNetworking.SendToAll(Mod.StartInteraction.CreateMessage(Block.From(interactable.BlockBehaviour)));
        }
        public void StopInteraction(Interactable interactable)
        {
            interactable.StopInteraction();
            ModNetworking.SendToAll(Mod.StopInteraction.CreateMessage(Block.From(interactable.BlockBehaviour)));
        }

        private void FixedUpdate()
        {
            if (inputDirection != Vector3.zero)
                _lastNonZeroDirection = inputDirection;

            if (inputRotation != Vector3.zero)
                _lastNonZeroRotation = inputRotation;

            if (IsLocal)
            {
                if (controlling)
                {
                    Quaternion xQuaternion = Quaternion.AngleAxis(rotationX, Vector3.up);
                    Quaternion yQuaternion = Quaternion.AngleAxis(rotationY, -Vector3.right);

                    ViewCamera.transform.localRotation = xQuaternion * yQuaternion;

                    MouseOrbit.Instance.camForward = ViewCamera.transform.forward;
                    MouseOrbit.Instance.camUp = ViewCamera.transform.up;

                    inputDirection = Vector3.ProjectOnPlane(ViewCamera.transform.forward, Vector3.up).normalized * Input.GetAxis("Vertical") + ViewCamera.transform.right * Input.GetAxis("Horizontal");
                    inputRotation = ViewCamera.transform.localEulerAngles;
                }
                else
                    inputDirection = inputRotation = Vector3.zero;

                Message directionMessage = Mod.SetControllerDirectionRotation.CreateMessage(Block.From(BlockBehaviour), inputDirection, inputRotation);
                ModNetworking.SendToHost(directionMessage);
            }

            if (IsServer && IsSimulating)
            {
                Rigidbody.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY;
                Rigidbody.angularDrag = 10;

                float timestep = Time.fixedDeltaTime;

                Vector3 groundVelocity = GetGroundVelocity();
                Vector3 bodyVelocity = Rigidbody.velocity;

                inputDirection = Vector3.ClampMagnitude(inputDirection, 1);
                Vector3 goalVel = groundVelocity + Vector3.Scale(inputDirection, new Vector3(1, 0, 1)) * speed.Value;

                goalVel = Vector3.MoveTowards(goalVel, goalVel + bodyVelocity, timestep);

                Vector3 neededAccel = (goalVel - bodyVelocity) / timestep;

                if (groundVelocity == Vector3.zero)
                    neededAccel = Vector3.ClampMagnitude(neededAccel, acceleration.Value);

                Rigidbody.AddForce(Vector3.Scale(neededAccel, new Vector3(1, 0, 1)) * Rigidbody.mass);

                Rigidbody.rotation = Quaternion.Euler(_originalRotation.x, _lastNonZeroRotation.y, _originalRotation.z);
                //Rigidbody.MoveRotation(Quaternion.RotateTowards(Rigidbody.rotation, 
                //    Quaternion.Euler(Rigidbody.rotation.eulerAngles.x, _lastNonZeroRotation.y, Rigidbody.rotation.eulerAngles.z), Time.fixedDeltaTime * 360));
            }
        }

        public void SetControlling(bool value)
        {
            controlling = value;

            Cursor.visible = !value;
            Cursor.lockState = value ? CursorLockMode.Locked : CursorLockMode.None;
            ViewCamera.fieldOfView = value ? fov.Value : _oldFov;
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

            if (Physics.Raycast(transform.position + Vector3.up, Vector2.down, out RaycastHit hit, 2F, layerMask))
                Rigidbody.AddForce(Vector3.up * jumpForce.Value, ForceMode.Impulse);
        }

        public Vector3 GetGroundVelocity()
        {
            if (Physics.Raycast(transform.position + Vector3.up, Vector2.down, out RaycastHit hit, 1F + groundStickDistance.Value, Game.BlockEntityLayerMask))
                if (hit.rigidbody != null)
                    return hit.rigidbody.velocity;
            return Vector3.zero;
        }
    }
}
