using System;
using System.Collections;
using Modding;
using Modding.Blocks;
using UnityEngine;

namespace FPSController
{
    public class Button : Interactable
    {
        public GameObject top;

        public MKey emulateKey;
        public MColourSlider color;
        public MToggle toggleMode;

        public MeshRenderer topRenderer;

        public Controller interactor;

        private MKey[] activationKeys = new MKey[0];
        private bool prevIsHeld;

        public bool isHeld;

        public override bool EmulatesAnyKeys => true;

        public bool IsServer => StatMaster.isHosting || !StatMaster.isMP || StatMaster.isLocalSim;

        public override void SafeAwake()
        {
            base.SafeAwake();
            emulateKey = AddEmulatorKey("On Pressed", "on-pressed", KeyCode.C);
            color = AddColourSlider("Color", "color", Color.white, false);
            toggleMode = AddToggle("Toggle", "toggle", false);
            color.ValueChanged += Color_ValueChanged;
            VisualController.MeshFilter.mesh = Mod.ButtonBase;

            if (!IsSimulating)
            {
                top = new GameObject("Top");
                top.transform.parent = transform;
                top.transform.localPosition = Vector3.zero;
                top.transform.localEulerAngles = Vector3.zero;
                top.AddComponent<MeshFilter>().mesh = Mod.ButtonTop;
                topRenderer = top.AddComponent<MeshRenderer>();
                topRenderer.material = VisualController.renderers[0].sharedMaterial;
            }
        }

        private void Start()
        {
            VisualController.MeshFilter.mesh = Mod.ButtonBase;
        }

        private void Color_ValueChanged(Color value)
        {
            topRenderer.material.color = color.Value;
        }

        public override void OnSimulateStart()
        {
            base.OnSimulateStart();

            BoxCollider interactCollider = gameObject.AddComponent<BoxCollider>();
            interactCollider.isTrigger = true;
            interactCollider.center = new Vector3(0, 0, 0.5F);
        }

        private void Update()
        {
            if (top != null)
            {
                top.transform.localPosition = Vector3.Lerp(top.transform.localPosition, isHeld ? Vector3.back * 0.1F : Vector3.zero, Time.deltaTime * 16);
                top.transform.localScale = Vector3.one;
                top.transform.localEulerAngles = Vector3.zero;
            }
        }

        public override void StartInteraction(Controller controller)
        {
            if (toggleMode.IsActive)
            {
                isHeld = !isHeld;
            }
            else if (interactor == null && !isHeld)
            {
                interactor = controller;
                isHeld = true;
            }
        }

        public override void StopInteraction(Controller controller)
        {
            if (controller != interactor)
                return;
            if (!isHeld)
                return;
            interactor = null;
            isHeld = false;
        }

        public override void SendKeyEmulationUpdateHost()
        {
            if (prevIsHeld != isHeld)
            {
                prevIsHeld = isHeld;
                EmulateKeys(activationKeys, emulateKey, isHeld);
            }
        }
    }
}
