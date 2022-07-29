using System;
using Modding;
using Modding.Blocks;
using UnityEngine;
using LocalMachine = Machine;

namespace FPSController
{
	public class Mod : ModEntryPoint
    {
        // camera block
        public static MessageType SetControllerDirectionRotation;
        public static MessageType Jump;
        public static MessageType StartInteraction;
        public static MessageType StopInteraction;
        public static PhysicMaterial maxFriction;

        public static Mesh ButtonBase, ButtonTop;

        public static Texture2D Crosshair;
        public static Texture2D HintBackground;
        public static Texture2D Fill;

        public static GUIStyle InfoText;
        public static GUIStyle InfoTextShadow;

        public static PhysicMaterial LowFriction;

        public override void OnLoad()
		{
            SetControllerDirectionRotation = ModNetworking.CreateMessageType(DataType.Block, DataType.Vector3, DataType.Vector3);
            Jump = ModNetworking.CreateMessageType(DataType.Block);
            StartInteraction = ModNetworking.CreateMessageType(DataType.Block, DataType.Block);
            StopInteraction = ModNetworking.CreateMessageType(DataType.Block, DataType.Block);

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

            LowFriction = new PhysicMaterial()
            {
                dynamicFriction = 0,
                staticFriction = 0
            };
        }

        private void OnMessageReceived(Message msg)
        {
            try
            {
                if (msg.Type == SetControllerDirectionRotation)
                    OnControllerMessage(msg);

                if (msg.Type == Jump)
                    OnJumpMessage(msg);

                if (msg.Type == StartInteraction)
                    OnStartInteractionMessage(msg);

                if (msg.Type == StopInteraction)
                    OnStopInteractionMessage(msg);
            } catch
            {
                // :(
            }
        }

        private void OnStopInteractionMessage(Message msg)
        {
            Block block = msg.GetData(0) as Block;
            Block block2 = msg.GetData(1) as Block;
            Controller controller = block.BlockScript as Controller;
            Interactable interactable = block2.BlockScript as Interactable;
            if (controller?.Machine.Player == msg.Sender)
                interactable?.StopInteraction(controller);
        }

        private void OnStartInteractionMessage(Message msg)
        {
            Block block = msg.GetData(0) as Block;
            Block block2 = msg.GetData(1) as Block;
            Controller controller = block.BlockScript as Controller;
            Interactable interactable = block2.BlockScript as Interactable;
            if (controller?.Machine.Player == msg.Sender)
                interactable?.StartInteraction(controller);
        }

        private void OnJumpMessage(Message msg)
        {
            Block block = msg.GetData(0) as Block;
            Controller controller = block.BlockScript as Controller;
            if (controller?.Machine.Player == msg.Sender)
                controller?.Jump();
        }

        private void OnControllerMessage(Message msg)
        {
            Block block = msg.GetData(0) as Block;
            Controller controller = block.BlockScript as Controller;
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
