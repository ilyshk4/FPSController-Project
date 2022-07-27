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

        private MKey[] activationKeys = new MKey[0];
        public bool isHeld;

        public override bool EmulatesAnyKeys => true;

        public override void SafeAwake()
        {
            base.SafeAwake();
            emulateKey = AddEmulatorKey("On Pressed", "on-pressed", KeyCode.C);
        }

        public override void OnSimulateStart()
        {
            base.OnSimulateStart();

            VisualController.MeshFilter.mesh = Mod.ButtonBase;

            top = new GameObject("Top");
            top.transform.parent = transform;
            top.transform.localPosition = Vector3.zero;
            top.transform.localEulerAngles = Vector3.zero;
            top.AddComponent<MeshFilter>().mesh = Mod.ButtonTop;
            top.AddComponent<MeshRenderer>().material = VisualController.renderers[0].sharedMaterial;

            BoxCollider interactCollider = gameObject.AddComponent<BoxCollider>();
            interactCollider.isTrigger = true;
            interactCollider.center = new Vector3(0, 0, 0.5F);
        }

        private void Update()
        {
            if (top != null)
                top.transform.localPosition = Vector3.Lerp(top.transform.localPosition, isHeld ? Vector3.back * 0.1F : Vector3.zero, Time.deltaTime * 4);
        }

        public override void StartInteraction()
        {
            if (StatMaster.isHosting || !StatMaster.isMP)
                EmulateKeys(activationKeys, emulateKey, true);
            isHeld = true;
        }

        public override void StopInteraction()
        {
            if (StatMaster.isHosting || !StatMaster.isMP)
                EmulateKeys(activationKeys, emulateKey, false);
            isHeld = false;
        }
    }
}
