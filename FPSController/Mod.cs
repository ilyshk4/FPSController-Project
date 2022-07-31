using System;
using Modding;
using Modding.Blocks;
using UnityEngine;
using LocalMachine = Machine;

namespace FPSController
{
	public class Mod : ModEntryPoint
    {
        // TODO: Fix local simulation non-lethal errors.
        // Fix mountain friction.
        // Crouching.
        // Health.

        public static MessageType SetControllerInput;
        public static MessageType Jump;
        public static MessageType RequestStartInteraction;
        public static MessageType RequestStopInteraction;
        public static MessageType RemoteStartInteraction;
        public static MessageType RemoteStopInteraction;
        public static MessageType SeatKeyPress;
        public static MessageType SeatKeyRelease;

        public static Mesh ButtonBase, ButtonTop;

        public static Texture2D Crosshair;
        public static Texture2D HintBackground;
        public static Texture2D Fill;

        public static GUIStyle InfoText;
        public static GUIStyle InfoTextShadow;

        public static PhysicMaterial NoFriction;

        public static MKey[] NoKeys;

        public static bool HasAuthority => StatMaster.isHosting || !StatMaster.isMP || StatMaster.isLocalSim;

        public override void OnLoad()
		{   
            SetControllerInput = ModNetworking.CreateMessageType(DataType.Block, DataType.Vector3, DataType.Vector3);
            Jump = ModNetworking.CreateMessageType(DataType.Block);

            RequestStartInteraction = ModNetworking.CreateMessageType(DataType.Block, DataType.Block);
            RequestStopInteraction = ModNetworking.CreateMessageType(DataType.Block, DataType.Block);
            RemoteStartInteraction = ModNetworking.CreateMessageType(DataType.Block, DataType.Block);
            RemoteStopInteraction = ModNetworking.CreateMessageType(DataType.Block, DataType.Block);

            SeatKeyPress = ModNetworking.CreateMessageType(DataType.Block, DataType.Integer);
            SeatKeyRelease = ModNetworking.CreateMessageType(DataType.Block, DataType.Integer);

            ModNetworking.MessageReceived += OnMessageReceived;

            Crosshair = ModResource.GetTexture("Crosshair");
            HintBackground = ModResource.GetTexture("HintBackground");
            ButtonBase = ModResource.GetMesh("ButtonBase");
            ButtonTop = ModResource.GetMesh("ButtonTop");

            Font font = null;
            try
            {
                font = GameObject.Find("HUD/OutOfBoundsWarning/VIs/Text")?.GetComponent<TextMesh>()?.font;
            }
            catch
            {
                Debug.LogWarning("Failed to hook GOST Common font. :(");
            }

            InfoText = new GUIStyle()
            {
                font = font,
                fontStyle = FontStyle.Bold,
                fontSize = 20,
                alignment = TextAnchor.MiddleCenter,
                normal =
                {
                    textColor = Color.white
                }
            };

            InfoTextShadow = new GUIStyle(InfoText);
            InfoTextShadow.normal.textColor = Color.black;

            NoFriction = new PhysicMaterial()
            {
                frictionCombine = PhysicMaterialCombine.Minimum,   
                dynamicFriction = 0,
                staticFriction = 0,
            };

            NoKeys = new MKey[0];
        }

        private void OnMessageReceived(Message msg)
        {
            try
            {
                if (msg.Type == SetControllerInput)
                    OnControllerMessage(msg);

                if (msg.Type == Jump)
                    OnJumpMessage(msg);

                if (msg.Type == RequestStartInteraction)
                    OnRequestStartInteraction(msg);

                if (msg.Type == RequestStopInteraction)
                    OnRequestStopInteraction(msg);

                if (msg.Type == RemoteStartInteraction)
                    OnRemoteStartInteraction(msg);

                if (msg.Type == RemoteStopInteraction)
                    OnRemoteStopInteraction(msg);

                if (msg.Type == SeatKeyPress)
                    OnSeatKeyPress(msg);

                if (msg.Type == SeatKeyRelease)
                    OnSeatKeyRelease(msg);

            } catch (Exception e)
            {
                Debug.LogWarning(e);
            }
        }

        private void OnSeatKeyPress(Message msg)
        {
            Block controllerBlock = (Block)msg.GetData(0);
            KeyCode key = (KeyCode)(int)msg.GetData(1);

            Controller controller = (Controller)controllerBlock?.BlockScript;

            if (controller?.Machine.Player == msg.Sender)
                controller.SetSeatKey(key, true);
        }

        private void OnSeatKeyRelease(Message msg)
        {
            Block controllerBlock = (Block)msg.GetData(0);
            KeyCode key = (KeyCode)(int)msg.GetData(1);

            Controller controller = (Controller)controllerBlock?.BlockScript;

            if (controller?.Machine.Player == msg.Sender)
                controller.SetSeatKey(key, false);
        }

        private static void GetInteractionData(Message msg, out Controller controller, out Interactable interactable)
        {
            Block controllerBlock = (Block)msg.GetData(0);
            Block targetBlock = (Block)msg.GetData(1);

            controller = (Controller)controllerBlock?.BlockScript;
            interactable = (Interactable)targetBlock?.BlockScript;
        }

        private void OnRemoteStartInteraction(Message msg)
        {
            GetInteractionData(msg, out Controller controller, out Interactable interactable);

            if (controller != null)
                interactable?.StartInteraction(controller);
        }

        private void OnRemoteStopInteraction(Message msg)
        {
            GetInteractionData(msg, out Controller controller, out Interactable interactable);

            if (controller != null)
                interactable?.StopInteraction();
        }

        private void OnRequestStartInteraction(Message msg)
        {
            GetInteractionData(msg, out Controller controller, out Interactable interactable);

            if (controller?.Machine.Player == msg.Sender && interactable != null && HasAuthority)
            {
                controller.TryStartInteraction(interactable);
            }
        }

        private void OnRequestStopInteraction(Message msg)
        {
            GetInteractionData(msg, out Controller controller, out Interactable interactable);

            if (controller?.Machine.Player == msg.Sender && interactable != null && HasAuthority)
            {
                controller.TryStopInteraction(interactable);
            }
        }
    
        private void OnJumpMessage(Message msg)
        {
            Block block = (Block)msg.GetData(0);

            Controller controller = (Controller)block.BlockScript;

            if (controller?.Machine.Player == msg.Sender || msg.Sender.IsHost)
                controller?.Jump();
        }

        private void OnControllerMessage(Message msg)
        {
            Block block = (Block)msg.GetData(0);
            Controller controller = (Controller)block?.BlockScript;

            Vector3 direction = (Vector3)msg.GetData(1);
            Vector3 rotation = (Vector3)msg.GetData(2);

            if (controller?.Machine.Player == msg.Sender)
                if (controller != null)
                {
                    controller.inputDirection = direction;
                    controller.inputRotation = rotation;
                }
        }
    }
}
