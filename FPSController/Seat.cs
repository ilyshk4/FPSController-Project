using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace FPSController
{
    public class Seat : Interactable
    {
        public static readonly KeyCode[] EmulatableKeys = new KeyCode[]
        {
            KeyCode.Mouse0, KeyCode.Mouse1, KeyCode.W, KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.Q, KeyCode.E, KeyCode.R, KeyCode.T, KeyCode.F, KeyCode.G, KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3
        };

        public class RemoteKey
        {
            public MKey emulator;
            public bool state, previousState;
        }

        public MKey emulateKey;
        public Dictionary<KeyCode, RemoteKey> remoteKeys;

        public override bool EmulatesAnyKeys => true;
        public bool Occupied => User != null;

        private bool _previousOccupied;

        public override void SafeAwake()
        {
            base.SafeAwake();

            emulateKey = AddEmulatorKey("Occupied", "occupied", KeyCode.F);

            remoteKeys = new Dictionary<KeyCode, RemoteKey>();

            foreach (var key in EmulatableKeys)
            {
                string code = key.ToString();
                remoteKeys.Add(key, new RemoteKey() { emulator = AddEmulatorKey(code, code, KeyCode.None) });
            }
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

            foreach (var key in remoteKeys.Values)
                key.state = false;
        }

        public void SetKey(KeyCode key, bool state)
        {
            if (User != null)
            {
                if (remoteKeys.TryGetValue(key, out RemoteKey remoteKey))
                    remoteKey.state = state;
            }
        }

        public override void SendKeyEmulationUpdateHost()
        {
            if (_previousOccupied != Occupied)
            {
                _previousOccupied = Occupied;
                EmulateKeys(Mod.NoKeys, emulateKey, Occupied);
            }

            foreach (var key in remoteKeys.Values)
            {
                if (key.state != key.previousState)
                {
                    EmulateKeys(Mod.NoKeys, key.emulator, key.state);
                    key.previousState = key.state;
                }
            }
        }
    }
}
