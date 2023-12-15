using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using GameNetcodeStuff;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

namespace FovAdjust {
	[BepInPlugin(modGUID, modName, modVer)]
	public class FovAdjustBase : BaseUnityPlugin {

		private const string modGUID = "Rozebud.FovAdjust";
		private const string modName = "FOV Adjust";
		private const string modVer = "1.1.2";

		private readonly Harmony harmony = new Harmony(modGUID);

		private static FovAdjustBase Instance;
		public static ManualLogSource log;

		public static ConfigEntry<float> configFov;
		public static ConfigEntry<bool> configHideVisor;

		public static bool inDebugMode = false;

		void Awake() {

			if (Instance == null) { Instance = this; }
			log = BepInEx.Logging.Logger.CreateLogSource(modGUID);

			log.LogInfo("Starting.");

			configFov = Config.Bind("General", "fov", 66f, "Change the field of view of the camera. Clamped from 66 to 130 for my sanity. Also keep in mind that this is vertical FOV.");
			configHideVisor = Config.Bind("General", "hideVisor", false, "Changes whether the first person visor is visible.");

			PlayerControllerBPatches.newTargetFovBase = Mathf.Clamp(configFov.Value, 66f, 130f);
			PlayerControllerBPatches.hideVisor = configHideVisor.Value;

			log.LogInfo("Configs DONE!");

			PlayerControllerBPatches.calculateVisorStuff();

			harmony.PatchAll(typeof(PlayerControllerBPatches));
			harmony.PatchAll(typeof(HUDManagerPatches));

			log.LogInfo("All FOV Adjust patches have loaded successfully.");

		}

	}



	//Patches for PlayerControllerB.
	public class PlayerControllerBPatches {

		public static float newTargetFovBase = 66f;
		static float prefixCamFov = 0f;

		public static bool hideVisor = false;

		static Vector3 visorScale;

		public static Vector3 visorScaleBottom = new Vector3(0.68f, 0.80f, 0.95f);
		public static Vector3 visorScaleTop = new Vector3(0.68f, 0.35f, 0.99f);
		public static float linToSinLerp = 0.6f;
		public static float visorScaleTopRefFOV = 130f;

		public static bool changeFovInTerminal = true;

		public static bool snapFovChange = false;

		public static float sprintFovMultiplier = 1.03f;

		[HarmonyPatch(typeof(PlayerControllerB), "Awake")]
		[HarmonyPostfix]
		static void Awake_Postfix(PlayerControllerB __instance) {

			if (filterPlayerControllers(__instance)) { return; }

			__instance.localVisor.localScale = visorScale;

		}

		[HarmonyPatch(typeof(PlayerControllerB), "Update")]
		[HarmonyPrefix]
		[HarmonyPriority(Priority.HigherThanNormal)]
		static void Update_Prefix(PlayerControllerB __instance) {

			if (filterPlayerControllers(__instance)) { return; }

			//Get the camera FOV before the Update function modifies it so I can do my own lerp later.
			prefixCamFov = __instance.gameplayCamera.fieldOfView;

			//DEBUG STUFF DO NOT WORRY!!!!!!
			if (FovAdjustBase.inDebugMode) {
				if (Keyboard.current.minusKey.wasPressedThisFrame) {
					visorScale.x -= 0.01f;
					FovAdjustBase.log.LogMessage(visorScale);
				}
				else if (Keyboard.current.equalsKey.wasPressedThisFrame) {
					visorScale.x += 0.01f;
					FovAdjustBase.log.LogMessage(visorScale);
				}

				if (Keyboard.current.leftBracketKey.wasPressedThisFrame) {
					visorScale.y -= 0.01f;
					FovAdjustBase.log.LogMessage(visorScale);
				}
				else if (Keyboard.current.rightBracketKey.wasPressedThisFrame) {
					visorScale.y += 0.01f;
					FovAdjustBase.log.LogMessage(visorScale);
				}

				if (Keyboard.current.semicolonKey.wasPressedThisFrame) {
					visorScale.z -= 0.01f;
					FovAdjustBase.log.LogMessage(visorScale);
				}
				else if (Keyboard.current.quoteKey.wasPressedThisFrame) {
					visorScale.z += 0.01f;
					FovAdjustBase.log.LogMessage(visorScale);
				}
			}

			if (__instance.localVisor.localScale != visorScale) {
				__instance.localVisor.localScale = visorScale;
			}

		}

		[HarmonyPatch(typeof(PlayerControllerB), "Update")]
		[HarmonyPostfix]
		[HarmonyPriority(Priority.LowerThanNormal)]
		static void Update_Postfix(PlayerControllerB __instance) {

			if (filterPlayerControllers(__instance)) { return; }

			//Copies what the Update function does to handle the FOV but it uses the custom values for normal gameplay.
			float finalTargetFov = newTargetFovBase;
			if (__instance.inTerminalMenu && changeFovInTerminal) { finalTargetFov = 60f; }
			else if (__instance.IsInspectingItem) { finalTargetFov = 46f; }
			else {
				if (__instance.isSprinting) { finalTargetFov *= sprintFovMultiplier; }
			}

			__instance.gameplayCamera.fieldOfView = Mathf.Lerp(prefixCamFov, finalTargetFov, 6f * Time.deltaTime);

			if (snapFovChange) {
				__instance.gameplayCamera.fieldOfView = finalTargetFov;
				prefixCamFov = finalTargetFov;
				snapFovChange = false;
			}

		}

		[HarmonyPatch(typeof(PlayerControllerB), "LateUpdate")]
		[HarmonyPostfix]
		[HarmonyPriority(Priority.LowerThanNormal)]
		static void LateUpdate_Postfix(PlayerControllerB __instance) {

			if (filterPlayerControllers(__instance)) { return; }

			if (newTargetFovBase > 66 || FovAdjustBase.inDebugMode) {
				__instance.localVisor.position = __instance.localVisor.position + (__instance.localVisor.rotation * new Vector3(0f, 0f, -0.06f));
			}

		}

		static float easeOutSine(float x) { return Mathf.Sin((x * Mathf.PI) / 2); }

		public static void calculateVisorStuff() {
			if (hideVisor) { visorScale = new Vector3(0f, 0f, 0f); }
			else {
				if (newTargetFovBase > 66 || FovAdjustBase.inDebugMode) {
					float visorLerpAmount = (newTargetFovBase - 66f) / (visorScaleTopRefFOV - 66f);
					visorLerpAmount = Mathf.Lerp(visorLerpAmount, easeOutSine(visorLerpAmount), linToSinLerp);
					visorScale = Vector3.LerpUnclamped(visorScaleBottom, visorScaleTop, visorLerpAmount);
				}
				else { visorScale = new Vector3(0.36f, 0.49f, 0.49f); }
			}
		}

		static bool filterPlayerControllers(PlayerControllerB player) {
			return (!player.IsOwner || !player.isPlayerControlled || ((player.IsServer && !player.isHostPlayerObject)) && !player.isTestingPlayer);
		}

	}



	//Patches for the HUDManager. For the chat commands.
	public class HUDManagerPatches {

		[HarmonyPatch(typeof(HUDManager), "SubmitChat_performed")]
		[HarmonyPrefix]
		public static bool SubmitChat_performed_Prefix(HUDManager __instance) {

			string command = __instance.chatTextField.text;

			//Set fov with chat command.
			if (command.StartsWith("/fov")) {

				string[] args = command.Split(' ');
				if (args.Length > 1) {

					if (float.TryParse(args[1], out float fovValue)) {
						if (!float.IsNaN(fovValue)) {
							fovValue = Mathf.Clamp(fovValue, 66, 130);
							PlayerControllerBPatches.newTargetFovBase = fovValue;
							PlayerControllerBPatches.snapFovChange = true;
							if (!FovAdjustBase.inDebugMode) { PlayerControllerBPatches.calculateVisorStuff(); }
						}
					}
				}
			}

			//Toggle visor with chat command.
			else if (command.StartsWith("/toggleVisor")) {

				PlayerControllerBPatches.hideVisor = !PlayerControllerBPatches.hideVisor;
				PlayerControllerBPatches.calculateVisorStuff();

			}

			//The rest of this is debug stuff to help me get visor scaling stuff.
			else if (command.StartsWith("/recalcVisor") && FovAdjustBase.inDebugMode) { PlayerControllerBPatches.calculateVisorStuff(); }
			else if (command.StartsWith("/setScaleBottom") && FovAdjustBase.inDebugMode) {
				string[] args = command.Split(' ');
				PlayerControllerBPatches.visorScaleBottom = new Vector3(float.Parse(args[1]), float.Parse(args[2]), float.Parse(args[3]));
			}
			else if (command.StartsWith("/setScaleTop") && FovAdjustBase.inDebugMode) {
				string[] args = command.Split(' ');
				PlayerControllerBPatches.visorScaleTop = new Vector3(float.Parse(args[1]), float.Parse(args[2]), float.Parse(args[3]));
			}
			else if (command.StartsWith("/setSinAmount") && FovAdjustBase.inDebugMode) {
				string[] args = command.Split(' ');
				PlayerControllerBPatches.linToSinLerp = float.Parse(args[1]);
			}
			else if (command.StartsWith("/setTopRef") && FovAdjustBase.inDebugMode) {
				string[] args = command.Split(' ');
				PlayerControllerBPatches.visorScaleTopRefFOV = float.Parse(args[1]);
			}
			else if (command.StartsWith("/gimmeMyValues") && FovAdjustBase.inDebugMode) {
				FovAdjustBase.log.LogMessage("visorScaleBottom: " + PlayerControllerBPatches.visorScaleBottom);
				FovAdjustBase.log.LogMessage("visorScaleTop: " + PlayerControllerBPatches.visorScaleTop);
				FovAdjustBase.log.LogMessage("linToSinLerp: " + PlayerControllerBPatches.linToSinLerp);
				FovAdjustBase.log.LogMessage("visorScaleTopRefFOV: " + PlayerControllerBPatches.visorScaleTopRefFOV);
			}

			//Skip to normal method if no commands are used.
			else { return true; }

			//Close out of chat box if a command is used.
			__instance.localPlayer = GameNetworkManager.Instance.localPlayerController;
			__instance.localPlayer.isTypingChat = false;
			__instance.chatTextField.text = "";
			EventSystem.current.SetSelectedGameObject(null);
			__instance.typingIndicator.enabled = false;

			return false;

		}

	}
}
