using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;
using static UnityEngine.ImageConversion;
using Photon.Pun;
using System.Reflection;

namespace MorePeak;

[BepInPlugin("com.smckeen.morepeak", "MorePeak", "1.8.1")]
public class MorePeakPlugin : BaseUnityPlugin {
	private static ManualLogSource ModLogger;
	private static ConfigEntry<string> selectedLevelConfig;
	private static ConfigEntry<bool> showCurrentLevelGUIConfig;
	private static ConfigEntry<bool> showSelectedLevelConfigGUIConfig;
	private static readonly Dictionary<int, float> lastRandomizeTime = new Dictionary<int, float>();
	private const float RANDOMIZE_COOLDOWN = 1.0f;

	// GUI variables
	private static string currentLevelName = "";
	private static bool showLevelGUI = false;
	private static Texture2D guiBackgroundTexture;
	private static GUIStyle guiStyle;

	// Settings GUI variables
	private static bool showSettingsGUI = false;
	private static Rect settingsWindowRect = new Rect(0, 0, 330, 200); // Will be positioned dynamically
	private static string tempSelectedLevel = "";
	private static bool tempShowCurrentLevel = true;
	private static bool tempShowSelectedLevelConfig = true;
	private static Vector2 scrollPosition = Vector2.zero;
	private static Texture2D settingsCogTexture;
	private static int onGUICallCount = 0;

	// Cached GUIStyles for performance
	private static GUIStyle cachedCogStyle;
	private static GUIStyle cachedLabelStyle;
	private static GUIStyle cachedTextFieldStyle;
	private static GUIStyle cachedButtonStyle;
	private static GUIStyle cachedToggleStyle;
	private static GUIStyle cachedWindowStyle;

	// Cached textures for performance
	private static Texture2D cachedDarkBackgroundTexture;
	private static Texture2D cachedButtonBackgroundTexture;
	private static Texture2D cachedButtonHoverTexture;
	private static Texture2D cachedButtonActiveTexture;
	private static Texture2D cachedTextFieldBackgroundTexture;
	private static Texture2D cachedWindowBackgroundTexture;

	void Awake() {
		ModLogger = Logger;

		// Load settings cog texture
		LoadSettingsCogTexture();

		// Configuration
		selectedLevelConfig = Config.Bind("Settings", "SelectedLevel", "Random",
			"Set to 'Daily' for the daily map, 'Random' for random levels, specify exact level name (e.g., 'Level_0'), or specify multiple levels separated by commas for random selection from that list (e.g., 'Level_0, Level_1, Level_2')");

		showCurrentLevelGUIConfig = Config.Bind("GUI", "ShowCurrentLevel", false,
			"Whether to show the current level name on screen during gameplay");

		showSelectedLevelConfigGUIConfig = Config.Bind("GUI", "ShowSelectedLevelConfig", false,
			"Whether to show the selected level configuration on screen during gameplay");

		ModLogger.LogInfo("MorePeak v1.8.1 loaded!");
		ModLogger.LogInfo("Config: SelectedLevel = " + selectedLevelConfig.Value);
		ModLogger.LogInfo("Config: ShowCurrentLevel = " + showCurrentLevelGUIConfig.Value);
		ModLogger.LogInfo("Config: ShowSelectedLevelConfig = " + showSelectedLevelConfigGUIConfig.Value);
		ModLogger.LogInfo("Available levels will be listed when you start a game.");

		var harmony = new Harmony("com.smckeen.morepeak");
		harmony.PatchAll();
	}

	void OnGUI() {
		onGUICallCount++;
		if (onGUICallCount % 60 == 0) { // Log every 60 frames (about once per second)
			ModLogger.LogDebug($"OnGUI called {onGUICallCount} times, showLevelGUI={showLevelGUI}");
		}

		// Ensure cached styles are initialized
		if (cachedCogStyle == null) {
			ModLogger.LogDebug("Initializing cached styles in OnGUI");
			InitializeCachedStyles();
		}

		// Draw settings cog button in top right
		DrawSettingsCog();

		// Draw settings window if expanded
		if (showSettingsGUI) {
			ModLogger.LogDebug("Drawing settings window");
			DrawSettingsWindow();
		}

		if (showLevelGUI && !string.IsNullOrEmpty(currentLevelName)) {
			// Ensure cached styles are initialized
			if (cachedCogStyle == null) {
				ModLogger.LogDebug("Initializing cached styles in OnGUI");
				InitializeCachedStyles();
			}

			// Always use cached background texture
			var backgroundTexture = cachedDarkBackgroundTexture ?? MakeTexture(2, 2, new Color(0, 0, 0, 0.7f));

			// Create or recreate guiStyle with the background texture
			if (guiStyle == null || guiStyle.normal.background != backgroundTexture) {
				guiStyle = new GUIStyle(GUI.skin.box);
				guiStyle.fontSize = 16;
				guiStyle.normal.textColor = Color.white;
				guiStyle.normal.background = backgroundTexture;
				guiStyle.padding = new RectOffset(10, 10, 5, 5);
			}

			float yOffset = 20; // Starting Y position
			float lineHeight = 30; // Height between lines

			// Show current level if enabled
			if (showCurrentLevelGUIConfig.Value) {
				string currentLevelText = "Current Level: " + currentLevelName;
				Vector2 currentLevelSize = guiStyle.CalcSize(new GUIContent(currentLevelText));
				float x = (Screen.width - currentLevelSize.x) / 2;
				GUI.Box(new Rect(x, yOffset, currentLevelSize.x, currentLevelSize.y), currentLevelText, guiStyle);
				yOffset += lineHeight;
			}

			// Show selected level config if enabled
			if (showSelectedLevelConfigGUIConfig.Value) {
				string selectedLevelText = "Config: " + selectedLevelConfig.Value;
				Vector2 selectedLevelSize = guiStyle.CalcSize(new GUIContent(selectedLevelText));
				float x = (Screen.width - selectedLevelSize.x) / 2;
				GUI.Box(new Rect(x, yOffset, selectedLevelSize.x, selectedLevelSize.y), selectedLevelText, guiStyle);
			}
		}
	}

	void OnDestroy() {
		// Clean up GUI resources when mod is unloaded
		if (guiBackgroundTexture != null) {
			DestroyImmediate(guiBackgroundTexture);
			guiBackgroundTexture = null;
		}
		if (settingsCogTexture != null) {
			DestroyImmediate(settingsCogTexture);
			settingsCogTexture = null;
		}

		// Clean up cached textures
		if (cachedDarkBackgroundTexture != null) {
			DestroyImmediate(cachedDarkBackgroundTexture);
			cachedDarkBackgroundTexture = null;
		}
		if (cachedButtonBackgroundTexture != null) {
			DestroyImmediate(cachedButtonBackgroundTexture);
			cachedButtonBackgroundTexture = null;
		}
		if (cachedButtonHoverTexture != null) {
			DestroyImmediate(cachedButtonHoverTexture);
			cachedButtonHoverTexture = null;
		}
		if (cachedButtonActiveTexture != null) {
			DestroyImmediate(cachedButtonActiveTexture);
			cachedButtonActiveTexture = null;
		}
		if (cachedTextFieldBackgroundTexture != null) {
			DestroyImmediate(cachedTextFieldBackgroundTexture);
			cachedTextFieldBackgroundTexture = null;
		}
		if (cachedWindowBackgroundTexture != null) {
			DestroyImmediate(cachedWindowBackgroundTexture);
			cachedWindowBackgroundTexture = null;
		}

		// Clean up cached styles
		guiStyle = null;
		cachedCogStyle = null;
		cachedLabelStyle = null;
		cachedTextFieldStyle = null;
		cachedButtonStyle = null;
		cachedToggleStyle = null;
		cachedWindowStyle = null;
	}

	// Helper method to create a colored texture for GUI background
	private Texture2D MakeTexture(int width, int height, Color color) {
		Color[] pixels = new Color[width * height];
		for (int i = 0; i < pixels.Length; i++) {
			pixels[i] = color;
		}
		Texture2D texture = new Texture2D(width, height);
		texture.SetPixels(pixels);
		texture.Apply();
		return texture;
	}

	[HarmonyPatch(typeof(MapBaker), "GetLevel")]
	static class MapBaker_GetLevel_Patch {
		static bool hasLoggedLevels = false;

		static bool Prefix(MapBaker __instance, int levelIndex, ref string __result) {
			try {
				if (__instance?.AllLevels == null || __instance.AllLevels.Length == 0) {
					return true; // Let original method handle it
				}

				// Log available levels once when first called
				if (!hasLoggedLevels) {
					ModLogger.LogInfo("=== AVAILABLE LEVELS ===");
					for (int i = 0; i < __instance.AllLevels.Length; i++) {
						string logScenePath = __instance.AllLevels[i]?.ScenePath ?? "Unknown";
						string levelName = System.IO.Path.GetFileNameWithoutExtension(logScenePath);
						ModLogger.LogInfo($"Level {i}: {levelName}");
					}
					ModLogger.LogInfo("========================");
					ModLogger.LogInfo("You can now set 'SelectedLevel' in the config file to any of these level names, or keep it as 'Random'");
					hasLoggedLevels = true;
				}

				// Only modify behavior if we're in multiplayer and are the master client,
				// or if we're in offline mode. This ensures only the decision-maker is affected.
				if (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient) {
					// Non-master clients should never call this method in the first place
					// according to the game's design, but if they do, let the original method run
					ModLogger.LogInfo("[CLIENT] Non-master client called GetLevel - using original method");
					return true;
				}

				// Apply cooldown to prevent excessive calls
				int instanceId = __instance.GetInstanceID();
				float currentTime = Time.time;
				if (lastRandomizeTime.ContainsKey(instanceId) &&
					currentTime - lastRandomizeTime[instanceId] < RANDOMIZE_COOLDOWN) {
					return true; // Let original method run
				}
				lastRandomizeTime[instanceId] = currentTime;

				// Clean up old entries to prevent dictionary from growing indefinitely
				if (lastRandomizeTime.Count > 10) {
					var keysToRemove = new List<int>();
					float cutoffTime = currentTime - 300f; // 5 minutes ago

					foreach (var kvp in lastRandomizeTime) {
						if (kvp.Value < cutoffTime) {
							keysToRemove.Add(kvp.Key);
						}
					}

					// Remove old entries in batch
					foreach (int key in keysToRemove) {
						lastRandomizeTime.Remove(key);
					}

					// Log cleanup if significant
					if (keysToRemove.Count > 0) {
						ModLogger.LogDebug($"Cleaned up {keysToRemove.Count} old randomize time entries");
					}
				}

				string configValue = selectedLevelConfig?.Value?.Trim() ?? "Random";
				string clientInfo = PhotonNetwork.InRoom ? "[MASTER]" : "[OFFLINE]";

				if (configValue.Equals("Daily", StringComparison.OrdinalIgnoreCase)) {
					// Use vanilla daily map - let original method handle it
					ModLogger.LogInfo($"{clientInfo} Using vanilla daily map");
					return true; // Let original method run
				} else if (configValue.Equals("Random", StringComparison.OrdinalIgnoreCase)) {
					// Random level selection
					int randomIndex = UnityEngine.Random.Range(0, __instance.AllLevels.Length);
					string randomScenePath = __instance.AllLevels[randomIndex]?.ScenePath ?? "";
					__result = System.IO.Path.GetFileNameWithoutExtension(randomScenePath);

					ModLogger.LogInfo($"{clientInfo} Random level selected: {__result} (index {randomIndex})");

					// Update GUI
					currentLevelName = __result;
					showLevelGUI = true;

					return false; // Skip original method
				} else if (configValue.Contains(",")) {
					// Multiple levels specified - pick random from the list
					string[] levelChoices = configValue.Split(',');
					List<string> validLevels = new List<string>();

					// Validate each level choice and collect valid ones
					foreach (string choice in levelChoices) {
						string trimmedChoice = choice.Trim();
						if (string.IsNullOrEmpty(trimmedChoice)) continue;

						// Check if this level exists
						for (int i = 0; i < __instance.AllLevels.Length; i++) {
							string searchScenePath = __instance.AllLevels[i]?.ScenePath ?? "";
							string levelName = System.IO.Path.GetFileNameWithoutExtension(searchScenePath);
							if (levelName.Equals(trimmedChoice, StringComparison.OrdinalIgnoreCase)) {
								validLevels.Add(levelName);
								break;
							}
						}
					}

					if (validLevels.Count > 0) {
						// Pick random from valid levels
						string selectedLevel = validLevels[UnityEngine.Random.Range(0, validLevels.Count)];
						__result = selectedLevel;

						ModLogger.LogInfo($"{clientInfo} Random level from list selected: {__result} (from {validLevels.Count} valid options)");

						// Update GUI
						currentLevelName = __result;
						showLevelGUI = true;

						return false; // Skip original method
					} else {
						// No valid levels found in the list, use random
						ModLogger.LogWarning($"No valid levels found in list '{configValue}'! Using random level instead.");
						int fallbackIndex = UnityEngine.Random.Range(0, __instance.AllLevels.Length);
						string fallbackScenePath = __instance.AllLevels[fallbackIndex]?.ScenePath ?? "";
						__result = System.IO.Path.GetFileNameWithoutExtension(fallbackScenePath);

						ModLogger.LogInfo($"{clientInfo} Fallback random level: {__result} (index {fallbackIndex})");

						// Update GUI
						currentLevelName = __result;
						showLevelGUI = true;

						return false; // Skip original method
					}
				} else {
					// Single specific level by name
					for (int i = 0; i < __instance.AllLevels.Length; i++) {
						string searchScenePath = __instance.AllLevels[i]?.ScenePath ?? "";
						string levelName = System.IO.Path.GetFileNameWithoutExtension(searchScenePath);
						if (levelName.Equals(configValue, StringComparison.OrdinalIgnoreCase)) {
							__result = levelName;

							ModLogger.LogInfo($"{clientInfo} Specific level selected: {__result} (index {i})");

							// Update GUI
							currentLevelName = __result;
							showLevelGUI = true;

							return false; // Skip original method
						}
					}

					// If specific level not found, log warning and use random
					ModLogger.LogWarning($"Level '{configValue}' not found! Using random level instead.");
					int fallbackIndex = UnityEngine.Random.Range(0, __instance.AllLevels.Length);
					string fallbackScenePath = __instance.AllLevels[fallbackIndex]?.ScenePath ?? "";
					__result = System.IO.Path.GetFileNameWithoutExtension(fallbackScenePath);

					ModLogger.LogInfo($"{clientInfo} Fallback random level: {__result} (index {fallbackIndex})");

					// Update GUI
					currentLevelName = __result;
					showLevelGUI = true;

					return false; // Skip original method
				}
			} catch (Exception ex) {
				ModLogger.LogError($"Error in MapBaker.GetLevel patch: {ex.Message}");
				return true; // Let original method handle it
			}
		}
	}

	// Patch to display the level name for all players when they receive the scene load RPC
	[HarmonyPatch(typeof(AirportCheckInKiosk), "BeginIslandLoadRPC")]
	static class AirportCheckInKiosk_BeginIslandLoadRPC_Patch {
		static void Prefix(string sceneName, int ascent) {
			try {
				// Update GUI for all clients when they receive the scene load command
				currentLevelName = sceneName;
				showLevelGUI = true;

				string clientInfo = PhotonNetwork.IsMasterClient ? "[MASTER]" : "[CLIENT]";
				ModLogger.LogInfo($"{clientInfo} Loading level: {sceneName} (ascent: {ascent})");
			} catch (Exception ex) {
				ModLogger.LogError($"Error in BeginIslandLoadRPC patch: {ex.Message}");
			}
		}
	}

	// Patch to clear GUI when returning to airport
	[HarmonyPatch(typeof(LoadingScreenHandler), "LoadSceneProcess")]
	static class LoadingScreenHandler_LoadSceneProcess_Patch {
		static void Prefix(string sceneName) {
			try {
				// Clear GUI when loading Airport scene (returning from a level)
				if (sceneName.Equals("Airport", StringComparison.OrdinalIgnoreCase)) {
					showLevelGUI = false;
					currentLevelName = "";
					ModLogger.LogInfo("Clearing level GUI - returning to Airport");

					// Clear guiStyle to force recreation with fresh background texture
					guiStyle = null;
				}
			} catch (Exception ex) {
				ModLogger.LogError($"Error in LoadSceneProcess patch: {ex.Message}");
			}
		}
	}

	// Additional patch for when players leave the room
	[HarmonyPatch(typeof(NetworkConnector), "OnLeftRoom")]
	static class NetworkConnector_OnLeftRoom_Patch {
		static void Postfix() {
			try {
				// Clear GUI when leaving multiplayer room
				showLevelGUI = false;
				currentLevelName = "";
				ModLogger.LogInfo("Clearing level GUI - left multiplayer room");
			} catch (Exception ex) {
				ModLogger.LogError($"Error in OnLeftRoom patch: {ex.Message}");
			}
		}
	}

	// Optional: Add some variety to biome variants within the same level
	[HarmonyPatch(typeof(LevelGeneration), "RandomizeBiomeVariants")]
	static class LevelGeneration_RandomizeBiomeVariants_Patch {
		static void Postfix() {
			try {
				// Just add a bit of extra randomization to biome variants
				UnityEngine.Random.InitState(UnityEngine.Random.Range(0, int.MaxValue));
			} catch (Exception ex) {
				ModLogger?.LogError($"Error in biome variant randomization: {ex.Message}");
			}
		}
	}

	private void DrawSettingsCog() {
		// Only show settings cog when not in a round
		if (showLevelGUI) {
			// Close settings window if it's open when entering a round
			if (showSettingsGUI) {
				showSettingsGUI = false;
			}
			ModLogger.LogDebug("Settings cog hidden - in a round (showLevelGUI = true)");
			return;
		}

		ModLogger.LogDebug("Drawing settings cog - not in a round");

		// Position in top right corner
		float cogSize = 32;
		float x = Screen.width - cogSize - 10;
		float y = 10;

		ModLogger.LogDebug($"Settings cog position: x={x}, y={y}, size={cogSize}");
		ModLogger.LogDebug($"Screen size: {Screen.width}x{Screen.height}");

		// Draw the settings cog button with the loaded texture
		if (settingsCogTexture != null) {
			ModLogger.LogDebug("Using settings cog texture");
			var styleToUse = cachedCogStyle ?? GUI.skin.button;
			if (GUI.Button(new Rect(x, y, cogSize, cogSize), settingsCogTexture, styleToUse)) {
				ModLogger.LogInfo("Settings cog clicked!");
				showSettingsGUI = !showSettingsGUI;

				// Initialize temp values when opening settings
				if (showSettingsGUI) {
					tempSelectedLevel = selectedLevelConfig.Value;
					tempShowCurrentLevel = showCurrentLevelGUIConfig.Value;
					tempShowSelectedLevelConfig = showSelectedLevelConfigGUIConfig.Value;

					// Position window below the button
					float windowX = x - 330 + cogSize; // Align right edge of window with right edge of button
					float windowY = y + cogSize + 5; // 5 pixels below the button

					// Ensure window stays within screen bounds
					if (windowX < 10) windowX = 10; // Keep at least 10px from left edge
					if (windowY + 200 > Screen.height - 10) windowY = Screen.height - 210; // Keep at least 10px from bottom edge

					settingsWindowRect = new Rect(windowX, windowY, 330, 200);
					ModLogger.LogInfo("Settings window opened");
				} else {
					ModLogger.LogInfo("Settings window closed");
				}
			}
		} else {
			ModLogger.LogDebug("Using fallback text cog (⚙)");
			// Fallback to text if texture failed to load
			var fallbackStyle = new GUIStyle(cachedCogStyle ?? GUI.skin.button);
			fallbackStyle.fontSize = 14;
			fallbackStyle.fontStyle = FontStyle.Bold;
			fallbackStyle.normal.textColor = Color.white;
			fallbackStyle.hover.textColor = Color.yellow;

			if (GUI.Button(new Rect(x, y, cogSize, cogSize), "⚙", fallbackStyle)) {
				ModLogger.LogInfo("Settings cog clicked!");
				showSettingsGUI = !showSettingsGUI;

				// Initialize temp values when opening settings
				if (showSettingsGUI) {
					tempSelectedLevel = selectedLevelConfig.Value;
					tempShowCurrentLevel = showCurrentLevelGUIConfig.Value;
					tempShowSelectedLevelConfig = showSelectedLevelConfigGUIConfig.Value;

					// Position window below the button
					float windowX = x - 330 + cogSize; // Align right edge of window with right edge of button
					float windowY = y + cogSize + 5; // 5 pixels below the button

					// Ensure window stays within screen bounds
					if (windowX < 10) windowX = 10; // Keep at least 10px from left edge
					if (windowY + 200 > Screen.height - 10) windowY = Screen.height - 210; // Keep at least 10px from bottom edge

					settingsWindowRect = new Rect(windowX, windowY, 330, 200);
					ModLogger.LogInfo("Settings window opened");
				} else {
					ModLogger.LogInfo("Settings window closed");
				}
			}
		}
	}

	private void DrawSettingsWindow() {
		// Create semi-transparent background with better contrast
		GUI.color = new Color(0, 0, 0, 0.9f);
		GUI.Box(new Rect(0, 0, Screen.width, Screen.height), "");
		GUI.color = Color.white;

		var windowStyle = cachedWindowStyle ?? GUI.skin.window;
		settingsWindowRect = GUI.Window(12345, settingsWindowRect, DrawSettingsWindowContents, "MorePeak Settings", windowStyle);
	}

	private void DrawSettingsWindowContents(int windowID) {
		float yPos = 30;
		float lineHeight = 25;
		float labelWidth = 150;
		float controlWidth = 150;

		// Get styles with fallbacks
		var labelStyle = cachedLabelStyle ?? GUI.skin.label;
		var textFieldStyle = cachedTextFieldStyle ?? GUI.skin.textField;
		var buttonStyle = cachedButtonStyle ?? GUI.skin.button;
		var toggleStyle = cachedToggleStyle ?? GUI.skin.toggle;

		// Selected Level Configuration
		GUI.Label(new Rect(10, yPos, labelWidth, 20), "Selected Level:", labelStyle);
		yPos += lineHeight;

		string newSelectedLevel = GUI.TextField(new Rect(10, yPos, controlWidth, 20), tempSelectedLevel, textFieldStyle);
		if (newSelectedLevel != tempSelectedLevel) {
			tempSelectedLevel = newSelectedLevel;
			// Apply immediately
			selectedLevelConfig.Value = tempSelectedLevel;
			Config.Save();
			ModLogger.LogInfo("SelectedLevel updated to: " + tempSelectedLevel);
		}
		yPos += lineHeight + 5;

		// Quick preset button for Random only
		if (GUI.Button(new Rect(10, yPos, 70, 20), "Random", buttonStyle)) {
			tempSelectedLevel = "Random";
			// Apply immediately
			selectedLevelConfig.Value = tempSelectedLevel;
			Config.Save();
			ModLogger.LogInfo("SelectedLevel set to Random");
		}

		// Daily preset button
		if (GUI.Button(new Rect(85, yPos, 70, 20), "Daily", buttonStyle)) {
			tempSelectedLevel = "Daily";
			// Apply immediately
			selectedLevelConfig.Value = tempSelectedLevel;
			Config.Save();
			ModLogger.LogInfo("SelectedLevel set to Daily");
		}
		yPos += lineHeight + 10;

		// GUI Display Options
		GUI.Label(new Rect(10, yPos, labelWidth, 20), "Display Options:", labelStyle);
		yPos += lineHeight;

		bool newShowCurrentLevel = GUI.Toggle(new Rect(10, yPos, controlWidth, 20), tempShowCurrentLevel, "Show Current Level", toggleStyle);
		if (newShowCurrentLevel != tempShowCurrentLevel) {
			tempShowCurrentLevel = newShowCurrentLevel;
			// Apply immediately
			showCurrentLevelGUIConfig.Value = tempShowCurrentLevel;
			Config.Save();
			ModLogger.LogInfo("ShowCurrentLevel updated to: " + tempShowCurrentLevel);
		}
		yPos += lineHeight;

		bool newShowSelectedLevelConfig = GUI.Toggle(new Rect(10, yPos, controlWidth, 20), tempShowSelectedLevelConfig, "Show Config", toggleStyle);
		if (newShowSelectedLevelConfig != tempShowSelectedLevelConfig) {
			tempShowSelectedLevelConfig = newShowSelectedLevelConfig;
			// Apply immediately
			showSelectedLevelConfigGUIConfig.Value = tempShowSelectedLevelConfig;
			Config.Save();
			ModLogger.LogInfo("ShowSelectedLevelConfig updated to: " + tempShowSelectedLevelConfig);
		}
		yPos += lineHeight + 10;

		// Close button (no Apply button needed since changes are applied immediately)
		if (GUI.Button(new Rect(10, yPos, 70, 20), "Close", buttonStyle)) {
			showSettingsGUI = false;
		}

		// Close button in top right of window
		if (GUI.Button(new Rect(settingsWindowRect.width - 25, 5, 20, 20), "X", buttonStyle)) {
			showSettingsGUI = false;
		}

		// Make window draggable
		GUI.DragWindow();
	}

	private void LoadSettingsCogTexture() {
		try {
			// Load the settings cog texture from embedded resource
			using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MorePeak.assets.settings-cog.png")) {
				if (stream != null) {
					byte[] imageData = new byte[stream.Length];
					stream.Read(imageData, 0, imageData.Length);

					settingsCogTexture = new Texture2D(2, 2);
					settingsCogTexture.LoadImage(imageData);
					ModLogger.LogInfo("Settings cog texture loaded successfully from embedded resource");
				} else {
					ModLogger.LogWarning("settings-cog.png embedded resource not found");
					// Create a fallback texture
					settingsCogTexture = MakeTexture(32, 32, Color.gray);
				}
			}
		} catch (Exception ex) {
			ModLogger.LogError("Error loading settings cog texture: " + ex.Message);
			// Create a fallback texture
			settingsCogTexture = MakeTexture(32, 32, Color.gray);
		}
	}

	private void InitializeCachedStyles() {
		ModLogger.LogDebug("Initializing cached styles and textures");

		// Create cached textures once
		cachedDarkBackgroundTexture = MakeTexture(2, 2, new Color(0, 0, 0, 0.7f));
		cachedButtonBackgroundTexture = MakeTexture(2, 2, new Color(0.2f, 0.2f, 0.2f, 0.8f));
		cachedButtonHoverTexture = MakeTexture(2, 2, new Color(0.3f, 0.3f, 0.3f, 0.9f));
		cachedButtonActiveTexture = MakeTexture(2, 2, new Color(0.4f, 0.4f, 0.4f, 0.9f));
		cachedTextFieldBackgroundTexture = MakeTexture(2, 2, new Color(0.2f, 0.2f, 0.2f, 0.9f));
		cachedWindowBackgroundTexture = MakeTexture(2, 2, new Color(0.1f, 0.1f, 0.1f, 0.95f));

		ModLogger.LogDebug("Cached textures created");

		// Create cached GUIStyles
		cachedCogStyle = new GUIStyle(GUI.skin.button);
		cachedCogStyle.normal.background = cachedButtonBackgroundTexture;
		cachedCogStyle.hover.background = cachedButtonHoverTexture;
		cachedCogStyle.active.background = cachedButtonActiveTexture;

		cachedLabelStyle = new GUIStyle(GUI.skin.label);
		cachedLabelStyle.fontSize = 12;
		cachedLabelStyle.wordWrap = true;
		cachedLabelStyle.normal.textColor = Color.white;

		cachedTextFieldStyle = new GUIStyle(GUI.skin.textField);
		cachedTextFieldStyle.fontSize = 12;
		cachedTextFieldStyle.normal.background = cachedTextFieldBackgroundTexture;
		cachedTextFieldStyle.normal.textColor = Color.white;

		cachedButtonStyle = new GUIStyle(GUI.skin.button);
		cachedButtonStyle.fontSize = 12;
		cachedButtonStyle.normal.background = cachedButtonBackgroundTexture;
		cachedButtonStyle.hover.background = cachedButtonHoverTexture;
		cachedButtonStyle.normal.textColor = Color.white;
		cachedButtonStyle.hover.textColor = Color.yellow;

		cachedToggleStyle = new GUIStyle(GUI.skin.toggle);
		cachedToggleStyle.fontSize = 12;
		cachedToggleStyle.normal.textColor = Color.white;

		cachedWindowStyle = new GUIStyle(GUI.skin.window);
		cachedWindowStyle.fontSize = 14;
		cachedWindowStyle.normal.background = cachedWindowBackgroundTexture;
		cachedWindowStyle.onNormal.background = cachedWindowBackgroundTexture;

		ModLogger.LogDebug("Cached styles created successfully");
	}
}
