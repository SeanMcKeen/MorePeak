using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Peak.Network;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using static UnityEngine.ImageConversion;
using MorePeak.MonoBehaviours;

namespace MorePeak;

[BepInPlugin("com.smckeen.morepeak", "MorePeak", "1.9.2")]
public class MorePeakPlugin : BaseUnityPlugin {
	// -------------------------------------------------------------------------
	// Static logger — exposed internally so MorePeakHUD can call LogVerbose too.
	// -------------------------------------------------------------------------
	internal static ManualLogSource ModLogger;

	// -------------------------------------------------------------------------
	// Configuration entries
	// -------------------------------------------------------------------------

	/// <summary>
	/// Which level(s) to load. Accepts: "Random", "Daily", a single level name,
	/// or a comma-separated list of level names for random selection from that pool.
	/// </summary>
	internal static ConfigEntry<string> selectedLevelConfig;

	/// <summary>Show the loaded level name on-screen during a round.</summary>
	internal static ConfigEntry<bool> showCurrentLevelGUIConfig;

	/// <summary>Show the current SelectedLevel config value on-screen during a round.</summary>
	internal static ConfigEntry<bool> showSelectedLevelConfigGUIConfig;

	/// <summary>
	/// When true, the mod emits detailed diagnostic messages every frame / on every
	/// significant internal event. Keep disabled for normal play to avoid log spam.
	/// </summary>
	internal static ConfigEntry<bool> verboseLoggingConfig;

	// -------------------------------------------------------------------------
	// Runtime state
	// -------------------------------------------------------------------------

	/// <summary>Name of the level that was most recently selected/received.</summary>
	internal static string currentLevelName = "";

	/// <summary>Controls whether the in-round level-name overlay is visible.</summary>
	private static bool showLevelGUI = false;

	// Per-instance cooldown dictionary to prevent GetLevel from being spammed
	// within a single frame (e.g. if the method is called by several callers).
	private const float RANDOMIZE_COOLDOWN = 1.0f;
	private static readonly Dictionary<int, float> lastRandomizeTime = new Dictionary<int, float>();

	// -------------------------------------------------------------------------
	// GUI — texture assets
	// -------------------------------------------------------------------------
	private static Texture2D guiBackgroundTexture;
	private static Texture2D settingsCogTexture;

	// -------------------------------------------------------------------------
	// GUI — settings window state
	// -------------------------------------------------------------------------
	private static bool showSettingsGUI = false;
	private static Rect settingsWindowRect = new Rect(0, 0, 330, 255);

	// Temporary mirrors of config values edited inside the window.
	private static string tempSelectedLevel = "";
	private static bool tempShowCurrentLevel = true;
	private static bool tempShowSelectedLevelConfig = true;
	private static bool tempVerboseLogging = false;

	private static Vector2 scrollPosition = Vector2.zero;
	private static int onGUICallCount = 0;

	// -------------------------------------------------------------------------
	// GUI — cached GUIStyles (created once on first OnGUI call)
	// -------------------------------------------------------------------------
	private static GUIStyle cachedCogStyle;
	private static GUIStyle cachedLabelStyle;
	private static GUIStyle cachedTextFieldStyle;
	private static GUIStyle cachedButtonStyle;
	private static GUIStyle cachedToggleStyle;
	private static GUIStyle cachedWindowStyle;

	// Cached solid-colour textures used by the styles above.
	private static Texture2D cachedDarkBackgroundTexture;
	private static Texture2D cachedButtonBackgroundTexture;
	private static Texture2D cachedButtonHoverTexture;
	private static Texture2D cachedButtonActiveTexture;
	private static Texture2D cachedTextFieldBackgroundTexture;
	private static Texture2D cachedWindowBackgroundTexture;

	// =========================================================================
	// Unity lifecycle
	// =========================================================================

	void Awake() {
		ModLogger = Logger;

		// Load the settings-cog icon from embedded resources.
		LoadSettingsCogTexture();

		// ----- Bind configuration entries -----
		selectedLevelConfig = Config.Bind(
			"Settings",
			"SelectedLevel",
			"Random",
			"Set to 'Daily' for the daily map, 'Random' for a fully random level, " +
			"an exact level name (e.g. 'Level_0'), or a comma-separated list for " +
			"random selection from that pool (e.g. 'Level_0, Level_1, Level_2').");

		showCurrentLevelGUIConfig = Config.Bind(
			"GUI",
			"ShowCurrentLevel",
			false,
			"Show the active level name in the top-right corner during a round.");

		showSelectedLevelConfigGUIConfig = Config.Bind(
			"GUI",
			"ShowSelectedLevelConfig",
			false,
			"Show the current SelectedLevel config value in the top-right corner during a round.");

		verboseLoggingConfig = Config.Bind(
			"Debug",
			"VerboseLogging",
			false,
			"Emit detailed diagnostic log messages. Leave disabled during normal play " +
			"to avoid log spam. Can also be toggled in-game via the settings cog.");

		ModLogger.LogInfo("MorePeak v1.9.2 loaded.");
		ModLogger.LogInfo($"Config  |  SelectedLevel={selectedLevelConfig.Value}" +
		                  $"  ShowCurrentLevel={showCurrentLevelGUIConfig.Value}" +
		                  $"  ShowSelectedLevelConfig={showSelectedLevelConfigGUIConfig.Value}" +
		                  $"  VerboseLogging={verboseLoggingConfig.Value}");
		ModLogger.LogInfo("Available levels will be listed in the log the first time a game is started.");

		var harmony = new Harmony("com.smckeen.morepeak");
		harmony.PatchAll();
		LogVerbose("Harmony patches applied successfully.");
	}

	void OnGUI() {
		onGUICallCount++;

		// Lazy-initialise cached styles on the very first OnGUI call.
		if (cachedCogStyle == null) {
			LogVerbose("First OnGUI call — initialising cached GUI styles.");
			InitializeCachedStyles();
		}

		if (onGUICallCount % 300 == 0) {
			LogVerbose($"OnGUI heartbeat — frame {onGUICallCount}, showLevelGUI={showLevelGUI}, showSettingsGUI={showSettingsGUI}");
		}

		DrawSettingsCog();

		if (showSettingsGUI) {
			DrawSettingsWindow();
		}
	}

	void OnDestroy() {
		LogVerbose("MorePeakPlugin.OnDestroy — releasing GUI resources.");

		// Destroy all textures to avoid GPU memory leaks when the mod is unloaded.
		DestroyTextureSafe(ref guiBackgroundTexture);
		DestroyTextureSafe(ref settingsCogTexture);
		DestroyTextureSafe(ref cachedDarkBackgroundTexture);
		DestroyTextureSafe(ref cachedButtonBackgroundTexture);
		DestroyTextureSafe(ref cachedButtonHoverTexture);
		DestroyTextureSafe(ref cachedButtonActiveTexture);
		DestroyTextureSafe(ref cachedTextFieldBackgroundTexture);
		DestroyTextureSafe(ref cachedWindowBackgroundTexture);

		// Null out cached styles so they get re-created if the mod reloads.
		cachedCogStyle = null;
		cachedLabelStyle = null;
		cachedTextFieldStyle = null;
		cachedButtonStyle = null;
		cachedToggleStyle = null;
		cachedWindowStyle = null;
	}

	// =========================================================================
	// Logging helpers
	// =========================================================================

	/// <summary>
	/// Logs a message only when verbose logging is enabled in config.
	/// Use this for high-frequency or diagnostic messages that would clutter
	/// the log for normal players.
	/// </summary>
	internal static void LogVerbose(string message) {
		if (verboseLoggingConfig?.Value == true) {
			ModLogger?.LogDebug($"[VERBOSE] {message}");
		}
	}

	// =========================================================================
	// Harmony patches
	// =========================================================================

	/// <summary>
	/// Patches <see cref="MapBaker.GetLevel"/> so that the host (or offline player)
	/// can override which level is loaded according to the SelectedLevel config.
	///
	/// Breaking change in game update: the backing array was renamed from
	/// <c>AllLevels</c> (field) to <c>ScenePaths</c> (field).  AllLevels still
	/// exists as an [Obsolete] property wrapper, but the compiled field reference
	/// in any mod built against the old DLL causes a MissingFieldException.
	/// This patch now correctly targets <c>ScenePaths</c>.
	///
	/// Also updated: host detection now uses <c>NetCode.Session.IsHost</c>
	/// (the game's own abstraction) instead of <c>PhotonNetwork.IsMasterClient</c>
	/// which the game itself has marked [Obsolete].
	/// </summary>
	[HarmonyPatch(typeof(MapBaker), "GetLevel")]
	static class MapBaker_GetLevel_Patch {
		// Log available levels only once per session.
		static bool hasLoggedLevels = false;

		static bool Prefix(MapBaker __instance, int levelIndex, ref string __result) {
			try {
				// Guard: nothing to do if the scene list is empty.
				if (__instance?.ScenePaths == null || __instance.ScenePaths.Length == 0) {
					LogVerbose("MapBaker.GetLevel called with empty ScenePaths — deferring to original.");
					return true; // Let original method handle it
				}

				// Log the full level list once so the user knows what names to use in config.
				if (!hasLoggedLevels) {
					ModLogger.LogInfo("=== AVAILABLE LEVELS ===");
					for (int i = 0; i < __instance.ScenePaths.Length; i++) {
						string logScenePath = __instance.ScenePaths[i] ?? "Unknown";
						string levelName = System.IO.Path.GetFileNameWithoutExtension(logScenePath);
						ModLogger.LogInfo($"  [{i}] {levelName}  (path: {logScenePath})");
					}
					ModLogger.LogInfo("========================");
					ModLogger.LogInfo("Set 'SelectedLevel' in the config (or via the in-game cog) to any level name above, or leave as 'Random'.");
					hasLoggedLevels = true;
				}

				// Non-host clients should never be calling GetLevel themselves; the host
				// broadcasts the chosen scene name via BeginIslandLoadRPC.
				// Using NetCode.Session.IsHost instead of the now-obsolete
				// PhotonNetwork.IsMasterClient for alignment with the game's own API.
				if (NetCode.Session.InRoom && !NetCode.Session.IsHost) {
					LogVerbose("[CLIENT] Non-host called GetLevel — deferring to original method.");
					return true;
				}

				// Per-instance cooldown: prevents excessive overrides when GetLevel is
				// called rapidly (e.g. multiple UI updates in the same frame).
				int instanceId = __instance.GetInstanceID();
				float currentTime = Time.time;
				if (lastRandomizeTime.TryGetValue(instanceId, out float lastTime) &&
					currentTime - lastTime < RANDOMIZE_COOLDOWN) {
					LogVerbose($"MapBaker.GetLevel on instance {instanceId} within cooldown window — deferring.");
					return true;
				}
				lastRandomizeTime[instanceId] = currentTime;

				// Periodically evict stale entries so the dictionary doesn't grow forever.
				if (lastRandomizeTime.Count > 10) {
					PruneRandomizeTimeCache(currentTime);
				}

				string configValue = selectedLevelConfig?.Value?.Trim() ?? "Random";
				string clientInfo = NetCode.Session.InRoom ? "[HOST]" : "[OFFLINE]";

				LogVerbose($"{clientInfo} MapBaker.GetLevel — configValue='{configValue}', levelIndex={levelIndex}");

				// ---- Daily: let the original method pick via the server-provided index ----
				if (configValue.Equals("Daily", StringComparison.OrdinalIgnoreCase)) {
					ModLogger.LogInfo($"{clientInfo} Using vanilla daily map (deferred to original GetLevel).");
					return true;
				}

				// ---- Fully random: pick any level at uniform random ----
				if (configValue.Equals("Random", StringComparison.OrdinalIgnoreCase)) {
					int randomIndex = UnityEngine.Random.Range(0, __instance.ScenePaths.Length);
					string randomScenePath = __instance.ScenePaths[randomIndex] ?? "";
					__result = System.IO.Path.GetFileNameWithoutExtension(randomScenePath);

					ModLogger.LogInfo($"{clientInfo} Random level selected: '{__result}' (index {randomIndex} of {__instance.ScenePaths.Length}).");
					currentLevelName = __result;
					showLevelGUI = true;
					return false; // Skip original method — we've set __result
				}

				// ---- Comma-separated list: pick random from the named pool ----
				if (configValue.Contains(",")) {
					return HandleLevelList(configValue, __instance, ref __result, clientInfo);
				}

				// ---- Single specific level by name ----
				return HandleSpecificLevel(configValue, __instance, ref __result, clientInfo);

			} catch (Exception ex) {
				ModLogger.LogError($"[MorePeak] Unhandled exception in MapBaker.GetLevel patch: {ex}");
				return true; // Always fall back to original on unexpected failure
			}
		}

		/// <summary>
		/// Handles a comma-separated list of level names by picking one at random from
		/// the subset that actually exists in <paramref name="instance"/>.ScenePaths.
		/// Falls back to a fully random level if none of the named levels are found.
		/// </summary>
		private static bool HandleLevelList(
			string configValue, MapBaker instance, ref string result, string clientInfo) {

			string[] levelChoices = configValue.Split(',');
			var validLevels = new List<string>();

			foreach (string choice in levelChoices) {
				string trimmedChoice = choice.Trim();
				if (string.IsNullOrEmpty(trimmedChoice)) continue;

				for (int i = 0; i < instance.ScenePaths.Length; i++) {
					string levelName = System.IO.Path.GetFileNameWithoutExtension(
						instance.ScenePaths[i] ?? "");
					if (levelName.Equals(trimmedChoice, StringComparison.OrdinalIgnoreCase)) {
						validLevels.Add(levelName);
						LogVerbose($"  Validated level from list: '{levelName}'.");
						break;
					}
				}
			}

			if (validLevels.Count > 0) {
				result = validLevels[UnityEngine.Random.Range(0, validLevels.Count)];
				ModLogger.LogInfo(
					$"{clientInfo} Random level from list selected: '{result}' " +
					$"({validLevels.Count} valid option(s) from the configured pool).");
			} else {
				// None of the listed names matched — fall back to a fully random level.
				ModLogger.LogWarning(
					$"[MorePeak] No valid levels found in list '{configValue}'. " +
					$"Falling back to fully random selection.");
				int fallbackIndex = UnityEngine.Random.Range(0, instance.ScenePaths.Length);
				result = System.IO.Path.GetFileNameWithoutExtension(
					instance.ScenePaths[fallbackIndex] ?? "");
				ModLogger.LogInfo(
					$"{clientInfo} Fallback random level: '{result}' (index {fallbackIndex}).");
			}

			currentLevelName = result;
			showLevelGUI = true;
			return false; // Skip original method
		}

		/// <summary>
		/// Handles a single exact level name.  Falls back to fully random if not found.
		/// </summary>
		private static bool HandleSpecificLevel(
			string configValue, MapBaker instance, ref string result, string clientInfo) {

			for (int i = 0; i < instance.ScenePaths.Length; i++) {
				string levelName = System.IO.Path.GetFileNameWithoutExtension(
					instance.ScenePaths[i] ?? "");
				if (levelName.Equals(configValue, StringComparison.OrdinalIgnoreCase)) {
					result = levelName;
					ModLogger.LogInfo(
						$"{clientInfo} Specific level selected: '{result}' (index {i}).");
					currentLevelName = result;
					showLevelGUI = true;
					return false; // Skip original method
				}
			}

			// Level name not found — warn and fall back to random.
			ModLogger.LogWarning(
				$"[MorePeak] Level '{configValue}' not found in ScenePaths. " +
				$"Falling back to random selection.");
			int fallbackIndex = UnityEngine.Random.Range(0, instance.ScenePaths.Length);
			result = System.IO.Path.GetFileNameWithoutExtension(
				instance.ScenePaths[fallbackIndex] ?? "");
			ModLogger.LogInfo(
				$"{clientInfo} Fallback random level: '{result}' (index {fallbackIndex}).");
			currentLevelName = result;
			showLevelGUI = true;
			return false; // Skip original method
		}

		/// <summary>
		/// Removes entries from <see cref="lastRandomizeTime"/> that are older than
		/// 5 minutes, preventing unbounded growth over long sessions.
		/// </summary>
		private static void PruneRandomizeTimeCache(float currentTime) {
			float cutoff = currentTime - 300f; // 5 minutes
			var toRemove = new List<int>();
			foreach (var kvp in lastRandomizeTime) {
				if (kvp.Value < cutoff) toRemove.Add(kvp.Key);
			}
			foreach (int key in toRemove) lastRandomizeTime.Remove(key);
			if (toRemove.Count > 0) {
				LogVerbose($"Pruned {toRemove.Count} stale entry/entries from randomize-time cache.");
			}
		}
	}

	/// <summary>
	/// Patches <see cref="AirportCheckInKiosk.BeginIslandLoadRPC"/> so that ALL
	/// connected clients (not just the host) update the on-screen level name when
	/// they receive the RPC to load a scene.
	///
	/// Updated: host detection uses <c>NetCode.Session.IsHost</c>.
	/// </summary>
	[HarmonyPatch(typeof(AirportCheckInKiosk), "BeginIslandLoadRPC")]
	static class AirportCheckInKiosk_BeginIslandLoadRPC_Patch {
		static void Prefix(string sceneName, int ascent) {
			try {
				currentLevelName = sceneName;
				showLevelGUI = true;

				// Use the game's own host-detection API rather than the now-obsolete
				// PhotonNetwork.IsMasterClient.
				string clientInfo = NetCode.Session.IsHost ? "[HOST]" : "[CLIENT]";
				ModLogger.LogInfo($"{clientInfo} BeginIslandLoadRPC received — loading level: '{sceneName}' (ascent: {ascent}).");
				LogVerbose($"{clientInfo} GUI updated: currentLevelName='{currentLevelName}', showLevelGUI=true.");
			} catch (Exception ex) {
				ModLogger.LogError($"[MorePeak] Exception in BeginIslandLoadRPC patch: {ex}");
			}
		}
	}

	/// <summary>
	/// Patches <see cref="LoadingScreenHandler.LoadSceneProcess"/> to clear the
	/// level-name overlay when the player returns to the Airport scene.
	/// </summary>
	[HarmonyPatch(typeof(LoadingScreenHandler), "LoadSceneProcess")]
	static class LoadingScreenHandler_LoadSceneProcess_Patch {
		static void Prefix(string sceneName) {
			try {
				if (sceneName.Equals("Airport", StringComparison.OrdinalIgnoreCase)) {
					showLevelGUI = false;
					currentLevelName = "";
					ModLogger.LogInfo("[MorePeak] Returning to Airport — level overlay cleared.");
					LogVerbose("LoadSceneProcess detected Airport load; showLevelGUI set to false.");
				} else {
					LogVerbose($"LoadSceneProcess called for scene '{sceneName}' (not Airport, no action).");
				}
			} catch (Exception ex) {
				ModLogger.LogError($"[MorePeak] Exception in LoadSceneProcess patch: {ex}");
			}
		}
	}

	/// <summary>
	/// Patches <see cref="NetworkConnector.OnLeftRoom"/> to clear the level-name
	/// overlay when the local player leaves the Photon room.
	/// </summary>
	[HarmonyPatch(typeof(NetworkConnector), "OnLeftRoom")]
	static class NetworkConnector_OnLeftRoom_Patch {
		static void Postfix() {
			try {
				showLevelGUI = false;
				currentLevelName = "";
				ModLogger.LogInfo("[MorePeak] Left multiplayer room — level overlay cleared.");
				LogVerbose("NetworkConnector.OnLeftRoom fired; GUI state reset.");
			} catch (Exception ex) {
				ModLogger.LogError($"[MorePeak] Exception in OnLeftRoom patch: {ex}");
			}
		}
	}

	/// <summary>
	/// Patches <see cref="LevelGeneration.RandomizeBiomeVariants"/> to re-seed
	/// Unity's RNG after the game has already randomised biome variants, giving
	/// a little extra variety within the same level.
	/// </summary>
	[HarmonyPatch(typeof(LevelGeneration), "RandomizeBiomeVariants")]
	static class LevelGeneration_RandomizeBiomeVariants_Patch {
		static void Postfix() {
			try {
				int newSeed = UnityEngine.Random.Range(0, int.MaxValue);
				UnityEngine.Random.InitState(newSeed);
				LogVerbose($"BiomeVariants randomised — RNG re-seeded with {newSeed}.");
			} catch (Exception ex) {
				ModLogger?.LogError($"[MorePeak] Exception in RandomizeBiomeVariants patch: {ex}");
			}
		}
	}

	/// <summary>
	/// Patches <see cref="GUIManager.Start"/> to inject a MorePeakHUD MonoBehaviour
	/// that renders the level/config overlay using the game's own TMP font.
	///
	/// The approach clones the existing AscentUI element (which already carries a
	/// TextMeshProUGUI with the correct font/material), strips the original
	/// <see cref="AscentUI"/> component, and attaches <see cref="MorePeakHUD"/>
	/// in its place.
	///
	/// If AscentUI is not found in the GUIManager hierarchy (e.g. the game moved it
	/// in a future update) the patch bails out gracefully with a warning.
	/// </summary>
	[HarmonyPatch(typeof(GUIManager), "Start")]
	static class GUIManagerPatch {
		[HarmonyPostfix]
		public static void Postfix(GUIManager __instance) {
			try {
				// Find AscentUI anywhere under the GUIManager hierarchy.
				var originalAscentUI = __instance.GetComponentInChildren<AscentUI>(includeInactive: true);

				if (originalAscentUI == null) {
					ModLogger.LogWarning(
						"[MorePeak] AscentUI not found in GUIManager children — " +
						"MorePeak HUD will not be shown. " +
						"If the game was recently updated, AscentUI may have been moved or renamed.");
					return;
				}

				LogVerbose($"Found AscentUI on '{originalAscentUI.gameObject.name}' — cloning for MorePeak HUD.");

				var originalTransform = originalAscentUI.transform;

				// Clone the AscentUI GameObject so we inherit its RectTransform settings,
				// TMP component, font material, and canvas hierarchy position.
				var hudRectTransform = (RectTransform)UnityEngine.Object.Instantiate(
					originalTransform, originalTransform.parent);
				hudRectTransform.name = "MorePeakLevelDisplay";

				// Remove the game's own AscentUI behaviour — we only want the TMP shell.
				var ascentComponent = hudRectTransform.GetComponent<AscentUI>();
				if (ascentComponent != null) {
					LogVerbose("Destroying cloned AscentUI component from MorePeak HUD object.");
					UnityEngine.Object.Destroy(ascentComponent);
				}

				// Attach our own HUD driver.
				hudRectTransform.gameObject.AddComponent<MorePeakHUD>();

				// Anchor to top-right so it never overlaps the AscentUI which sits elsewhere.
				hudRectTransform.pivot = hudRectTransform.anchorMax = hudRectTransform.anchorMin =
					new Vector2(1f, 1f);
				hudRectTransform.anchoredPosition = Vector2.zero;

				ModLogger.LogInfo("[MorePeak] HUD initialised successfully.");
				LogVerbose($"MorePeakHUD anchored to top-right of '{hudRectTransform.parent.name}'.");
			} catch (Exception ex) {
				ModLogger.LogError($"[MorePeak] Exception while initialising HUD in GUIManager.Start patch: {ex}");
			}
		}
	}

	// =========================================================================
	// GUI drawing helpers
	// =========================================================================

	/// <summary>
	/// Draws the small settings-cog button in the top-right corner.
	/// Hidden during active rounds so it doesn't clutter the view.
	/// </summary>
	private void DrawSettingsCog() {
		// Hide (and close) the settings window while inside a round.
		if (showLevelGUI) {
			if (showSettingsGUI) {
				showSettingsGUI = false;
				LogVerbose("Settings window auto-closed because a round started.");
			}
			return;
		}

		float cogSize = 32f;
		float x = Screen.width - cogSize - 10f;
		float y = 10f;

		var styleToUse = cachedCogStyle ?? GUI.skin.button;

		bool clicked = settingsCogTexture != null
			? GUI.Button(new Rect(x, y, cogSize, cogSize), settingsCogTexture, styleToUse)
			: GUI.Button(new Rect(x, y, cogSize, cogSize), "⚙", BuildFallbackCogStyle());

		if (clicked) {
			showSettingsGUI = !showSettingsGUI;
			LogVerbose($"Settings cog clicked — settings window is now {(showSettingsGUI ? "open" : "closed")}.");

			if (showSettingsGUI) {
				// Mirror current config values into the temporary editing fields.
				tempSelectedLevel = selectedLevelConfig.Value;
				tempShowCurrentLevel = showCurrentLevelGUIConfig.Value;
				tempShowSelectedLevelConfig = showSelectedLevelConfigGUIConfig.Value;
				tempVerboseLogging = verboseLoggingConfig.Value;

				// Position the window below and flush-right with the cog button.
				float windowX = x - 330f + cogSize;
				float windowY = y + cogSize + 5f;
				if (windowX < 10f) windowX = 10f;
				if (windowY + 255f > Screen.height - 10f) windowY = Screen.height - 265f;
				settingsWindowRect = new Rect(windowX, windowY, 330f, 255f);
			}
		}
	}

	/// <summary>
	/// Builds a minimal fallback style for the cog button when the texture
	/// asset failed to load.
	/// </summary>
	private GUIStyle BuildFallbackCogStyle() {
		var style = new GUIStyle(cachedCogStyle ?? GUI.skin.button);
		style.fontSize = 14;
		style.fontStyle = FontStyle.Bold;
		style.normal.textColor = Color.white;
		style.hover.textColor = Color.yellow;
		return style;
	}

	/// <summary>Renders the translucent settings panel.</summary>
	private void DrawSettingsWindow() {
		// Full-screen dim behind the window for readability.
		GUI.color = new Color(0f, 0f, 0f, 0.85f);
		GUI.Box(new Rect(0f, 0f, Screen.width, Screen.height), "");
		GUI.color = Color.white;

		var windowStyle = cachedWindowStyle ?? GUI.skin.window;
		settingsWindowRect = GUI.Window(12345, settingsWindowRect, DrawSettingsWindowContents,
			"MorePeak Settings", windowStyle);
	}

	/// <summary>Draws the contents of the draggable settings window.</summary>
	private void DrawSettingsWindowContents(int windowID) {
		float yPos = 30f;
		const float LINE_HEIGHT = 25f;
		const float LABEL_WIDTH = 150f;
		const float CTRL_WIDTH = 150f;

		var labelStyle = cachedLabelStyle ?? GUI.skin.label;
		var textFieldStyle = cachedTextFieldStyle ?? GUI.skin.textField;
		var buttonStyle = cachedButtonStyle ?? GUI.skin.button;
		var toggleStyle = cachedToggleStyle ?? GUI.skin.toggle;

		// ----- Selected Level -----
		GUI.Label(new Rect(10f, yPos, LABEL_WIDTH, 20f), "Selected Level:", labelStyle);
		yPos += LINE_HEIGHT;

		string newSelectedLevel = GUI.TextField(
			new Rect(10f, yPos, CTRL_WIDTH, 20f), tempSelectedLevel, textFieldStyle);
		if (newSelectedLevel != tempSelectedLevel) {
			tempSelectedLevel = newSelectedLevel;
			selectedLevelConfig.Value = tempSelectedLevel;
			Config.Save();
			LogVerbose($"SelectedLevel updated via GUI to: '{tempSelectedLevel}'.");
		}
		yPos += LINE_HEIGHT + 5f;

		// Quick-preset buttons: Random and Daily
		if (GUI.Button(new Rect(10f, yPos, 70f, 20f), "Random", buttonStyle)) {
			tempSelectedLevel = "Random";
			selectedLevelConfig.Value = "Random";
			Config.Save();
			LogVerbose("SelectedLevel set to 'Random' via quick-preset button.");
		}
		if (GUI.Button(new Rect(85f, yPos, 70f, 20f), "Daily", buttonStyle)) {
			tempSelectedLevel = "Daily";
			selectedLevelConfig.Value = "Daily";
			Config.Save();
			LogVerbose("SelectedLevel set to 'Daily' via quick-preset button.");
		}
		yPos += LINE_HEIGHT + 10f;

		// ----- Display Options -----
		GUI.Label(new Rect(10f, yPos, LABEL_WIDTH, 20f), "Display Options:", labelStyle);
		yPos += LINE_HEIGHT;

		bool newShowCurrentLevel = GUI.Toggle(
			new Rect(10f, yPos, CTRL_WIDTH, 20f),
			tempShowCurrentLevel, "Show Current Level", toggleStyle);
		if (newShowCurrentLevel != tempShowCurrentLevel) {
			tempShowCurrentLevel = newShowCurrentLevel;
			showCurrentLevelGUIConfig.Value = tempShowCurrentLevel;
			Config.Save();
			LogVerbose($"ShowCurrentLevel toggled to {tempShowCurrentLevel} via GUI.");
		}
		yPos += LINE_HEIGHT;

		bool newShowSelectedLevelConfig = GUI.Toggle(
			new Rect(10f, yPos, CTRL_WIDTH, 20f),
			tempShowSelectedLevelConfig, "Show Config", toggleStyle);
		if (newShowSelectedLevelConfig != tempShowSelectedLevelConfig) {
			tempShowSelectedLevelConfig = newShowSelectedLevelConfig;
			showSelectedLevelConfigGUIConfig.Value = tempShowSelectedLevelConfig;
			Config.Save();
			LogVerbose($"ShowSelectedLevelConfig toggled to {tempShowSelectedLevelConfig} via GUI.");
		}
		yPos += LINE_HEIGHT + 10f;

		// ----- Debug Options -----
		GUI.Label(new Rect(10f, yPos, LABEL_WIDTH, 20f), "Debug Options:", labelStyle);
		yPos += LINE_HEIGHT;

		bool newVerboseLogging = GUI.Toggle(
			new Rect(10f, yPos, CTRL_WIDTH, 20f),
			tempVerboseLogging, "Verbose Logging", toggleStyle);
		if (newVerboseLogging != tempVerboseLogging) {
			tempVerboseLogging = newVerboseLogging;
			verboseLoggingConfig.Value = tempVerboseLogging;
			Config.Save();
			// This message is always logged (not guarded by verbose) so the player
			// gets confirmation that the toggle worked.
			ModLogger.LogInfo($"[MorePeak] Verbose logging {(tempVerboseLogging ? "enabled" : "disabled")} via in-game settings.");
		}
		yPos += LINE_HEIGHT + 10f;

		// ----- Close button -----
		if (GUI.Button(new Rect(10f, yPos, 70f, 20f), "Close", buttonStyle)) {
			showSettingsGUI = false;
			LogVerbose("Settings window closed via Close button.");
		}

		// X button in the title bar area
		if (GUI.Button(new Rect(settingsWindowRect.width - 25f, 5f, 20f, 20f), "X", buttonStyle)) {
			showSettingsGUI = false;
			LogVerbose("Settings window closed via X button.");
		}

		GUI.DragWindow();
	}

	// =========================================================================
	// Texture / style helpers
	// =========================================================================

	/// <summary>
	/// Attempts to load the settings-cog PNG from embedded resources.
	/// Falls back to a flat grey square if the asset is missing.
	/// </summary>
	private void LoadSettingsCogTexture() {
		try {
			using var stream = Assembly.GetExecutingAssembly()
				.GetManifestResourceStream("MorePeak.assets.settings-cog.png");

			if (stream != null) {
				byte[] imageData = new byte[stream.Length];
				stream.Read(imageData, 0, imageData.Length);
				settingsCogTexture = new Texture2D(2, 2);
				settingsCogTexture.LoadImage(imageData);
				LogVerbose("settings-cog.png loaded from embedded resources.");
			} else {
				ModLogger.LogWarning("[MorePeak] Embedded resource 'MorePeak.assets.settings-cog.png' not found — using fallback grey square.");
				settingsCogTexture = MakeTexture(32, 32, Color.gray);
			}
		} catch (Exception ex) {
			ModLogger.LogError($"[MorePeak] Failed to load settings-cog texture: {ex.Message}");
			settingsCogTexture = MakeTexture(32, 32, Color.gray);
		}
	}

	/// <summary>
	/// Creates and caches all GUIStyles and their backing textures.
	/// Called lazily on the first OnGUI frame so that <c>GUI.skin</c> is valid.
	/// </summary>
	private void InitializeCachedStyles() {
		cachedDarkBackgroundTexture    = MakeTexture(2, 2, new Color(0.00f, 0.00f, 0.00f, 0.70f));
		cachedButtonBackgroundTexture  = MakeTexture(2, 2, new Color(0.20f, 0.20f, 0.20f, 0.80f));
		cachedButtonHoverTexture       = MakeTexture(2, 2, new Color(0.30f, 0.30f, 0.30f, 0.90f));
		cachedButtonActiveTexture      = MakeTexture(2, 2, new Color(0.40f, 0.40f, 0.40f, 0.90f));
		cachedTextFieldBackgroundTexture = MakeTexture(2, 2, new Color(0.20f, 0.20f, 0.20f, 0.90f));
		cachedWindowBackgroundTexture  = MakeTexture(2, 2, new Color(0.10f, 0.10f, 0.10f, 0.95f));

		cachedCogStyle = new GUIStyle(GUI.skin.button) {
			normal  = { background = cachedButtonBackgroundTexture },
			hover   = { background = cachedButtonHoverTexture },
			active  = { background = cachedButtonActiveTexture },
		};

		cachedLabelStyle = new GUIStyle(GUI.skin.label) {
			fontSize = 12,
			wordWrap = true,
			normal   = { textColor = Color.white },
		};

		cachedTextFieldStyle = new GUIStyle(GUI.skin.textField) {
			fontSize = 12,
			normal   = { background = cachedTextFieldBackgroundTexture, textColor = Color.white },
		};

		cachedButtonStyle = new GUIStyle(GUI.skin.button) {
			fontSize = 12,
			normal   = { background = cachedButtonBackgroundTexture, textColor = Color.white },
			hover    = { background = cachedButtonHoverTexture, textColor = Color.yellow },
		};

		cachedToggleStyle = new GUIStyle(GUI.skin.toggle) {
			fontSize = 12,
			normal   = { textColor = Color.white },
		};

		cachedWindowStyle = new GUIStyle(GUI.skin.window) {
			fontSize = 14,
			normal   = { background = cachedWindowBackgroundTexture },
			onNormal = { background = cachedWindowBackgroundTexture },
		};

		LogVerbose("Cached GUI styles and textures initialised.");
	}

	/// <summary>Creates a solid-colour <see cref="Texture2D"/> of the given size.</summary>
	private Texture2D MakeTexture(int width, int height, Color color) {
		Color[] pixels = new Color[width * height];
		for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
		var texture = new Texture2D(width, height);
		texture.SetPixels(pixels);
		texture.Apply();
		return texture;
	}

	/// <summary>Safely destroys a texture and sets the reference to null.</summary>
	private static void DestroyTextureSafe(ref Texture2D texture) {
		if (texture != null) {
			DestroyImmediate(texture);
			texture = null;
		}
	}
}
