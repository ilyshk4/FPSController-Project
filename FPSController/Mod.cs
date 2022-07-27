using System;
using Modding;
using Modding.Blocks;
using UnityEngine;
using LocalMachine = Machine;

namespace FPSController
{
	public class Mod : ModEntryPoint
    {
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

        public static Mesh CharacterMesh;
        public static Material CharacterMaterial;

        public override void OnLoad()
		{
            SetControllerDirectionRotation = ModNetworking.CreateMessageType(DataType.Block, DataType.Vector3, DataType.Vector3);
            Jump = ModNetworking.CreateMessageType(DataType.Block);
            StartInteraction = ModNetworking.CreateMessageType(DataType.Block);
            StopInteraction = ModNetworking.CreateMessageType(DataType.Block);

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

            GameObject peasant = GameObject.Find("_PERSISTENT/OBJECTS/Prefabs/Humans/PeasantV2");

            CharacterMesh = peasant.GetComponentInChildren<MeshFilter>().mesh;
            CharacterMaterial = peasant.GetComponentInChildren<MeshRenderer>().sharedMaterial;

            Debug.Log(peasant);
        }

        private void OnMessageReceived(Message msg)
        {
            if (msg.Type == SetControllerDirectionRotation)
            {
                Block block = msg.GetData(0) as Block;
                Controller controller = block.BlockScript as Controller;
                Vector3 direction = (Vector3)msg.GetData(1);
                Vector3 rotation = (Vector3)msg.GetData(2);
                controller.inputDirection = direction;
                controller.inputRotation = rotation;
            }

            if (msg.Type == Jump)
            {
                Block block = msg.GetData(0) as Block;
                Controller controller = block.BlockScript as Controller;
                controller.Jump();
            }

            if (msg.Type == StartInteraction)
            {
                Block block = msg.GetData(0) as Block;
                Interactable interactable = block.BlockScript as Interactable;
                interactable.StartInteraction();
            }

            if (msg.Type == StopInteraction)
            {
                Block block = msg.GetData(0) as Block;
                Interactable interactable = block.BlockScript as Interactable;
                interactable.StopInteraction();
            }
        }
    }
}
