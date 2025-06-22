using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;


namespace MorePeak {
    [BepInPlugin("com.smckeen.morepeak", "MorePeak", "1.7.0")]
    public class MorePeakPlugin : BaseUnityPlugin {
        private static ManualLogSource ModLogger;
        private static ConfigEntry<string> selectedLevelConfig;
        private static ConfigEntry<bool> showCurrentLevelGUIConfig;
        private static ConfigEntry<bool> showSelectedLevelConfigGUIConfig;
        private static Dictionary<int, float> lastRandomizeTime = new Dictionary<int, float>();
        private const float RANDOMIZE_COOLDOWN = 1.0f;

        // GUI variables
        private static string currentLevelName = "";
        private static bool showLevelGUI = false;
        private static Texture2D guiBackgroundTexture;
        private static GUIStyle guiStyle;

        void Awake() {
            ModLogger = Logger;

            // Configuration
            selectedLevelConfig = Config.Bind("Settings", "SelectedLevel", "Random",
                "Set to 'Random' for random levels, specify exact level name (e.g., 'Level_0'), or specify multiple levels separated by commas for random selection from that list (e.g., 'Level_0, Level_1, Level_2')");
            
            showCurrentLevelGUIConfig = Config.Bind("GUI", "ShowCurrentLevel", false,
                "Whether to show the current level name on screen during gameplay");
            
            showSelectedLevelConfigGUIConfig = Config.Bind("GUI", "ShowSelectedLevelConfig", false,
                "Whether to show the selected level configuration on screen during gameplay");

            ModLogger.LogInfo("MorePeak v1.7.0 loaded!");
            ModLogger.LogInfo("Config: SelectedLevel = " + selectedLevelConfig.Value);
            ModLogger.LogInfo("Config: ShowCurrentLevel = " + showCurrentLevelGUIConfig.Value);
            ModLogger.LogInfo("Config: ShowSelectedLevelConfig = " + showSelectedLevelConfigGUIConfig.Value);
            ModLogger.LogInfo("Available levels will be listed when you start a game.");

            var harmony = new Harmony("com.smckeen.morepeak");
            harmony.PatchAll();
        }

        void OnGUI() {
            if (showLevelGUI && !string.IsNullOrEmpty(currentLevelName)) {
                // Initialize GUI resources only once
                if (guiBackgroundTexture == null) {
                    guiBackgroundTexture = MakeTexture(2, 2, new Color(0, 0, 0, 0.7f));
                }

                if (guiStyle == null) {
                    guiStyle = new GUIStyle(GUI.skin.box);
                    guiStyle.fontSize = 16;
                    guiStyle.normal.textColor = Color.white;
                    guiStyle.normal.background = guiBackgroundTexture;
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
            guiStyle = null;
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
                        foreach (var kvp in lastRandomizeTime) {
                            if (currentTime - kvp.Value > 300f) // Remove entries older than 5 minutes
                            {
                                keysToRemove.Add(kvp.Key);
                            }
                        }
                        foreach (int key in keysToRemove) {
                            lastRandomizeTime.Remove(key);
                        }
                    }

                    string configValue = selectedLevelConfig?.Value?.Trim() ?? "Random";
                    string clientInfo = PhotonNetwork.InRoom ? "[MASTER]" : "[OFFLINE]";

                    if (configValue.Equals("Random", StringComparison.OrdinalIgnoreCase)) {
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
                    }

                    // Also clear GUI resources when loading any new scene to prevent buildup
                    if (guiBackgroundTexture != null) {
                        DestroyImmediate(guiBackgroundTexture);
                        guiBackgroundTexture = null;
                    }
                    guiStyle = null;
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
    }
}