using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace FPSController
{
    public class Seat : Interactable
    {
        public MKey emulateKey;

        public override bool EmulatesAnyKeys => true;
        public bool Occupied => User != null;

        private bool _previousOccupied;

        public override void SafeAwake()
        {
            base.SafeAwake();

            emulateKey = AddEmulatorKey("Occupied", "occupied", KeyCode.F);
        }

        public override void OnSimulateStart()
        {
            base.OnSimulateStart();

            BoxCollider interactCollider = gameObject.AddComponent<BoxCollider>();
            interactCollider.isTrigger = true;
            interactCollider.size = new Vector3(1.1F, 1.1F, 0.6F);
            interactCollider.center = new Vector3(0, 0, 0.25F);
        }

        public override void StartInteraction(Controller controller)
        {
            controller.SetSeat(this);
            User = controller;
        }

        public override void OnSimulateStop()
        {
            if (User != null)
                User.SetSeat(null);
        }

        public void SetFree()
        {
            User = null;
        }

        public override void SendKeyEmulationUpdateHost()
        {
            if (_previousOccupied != Occupied)
            {
                _previousOccupied = Occupied;
                EmulateKeys(Mod.NoKeys, emulateKey, Occupied);
            }
        }
    }
}
