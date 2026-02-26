using System.Collections.Generic;
using UnityEngine;
using TMPro;

namespace MorePeak.MonoBehaviours;

/// <summary>
/// Drives the on-screen level/config overlay that MorePeak injects into the
/// GUIManager's canvas hierarchy during <c>GUIManager.Start</c>.
///
/// The component is attached to a clone of the AscentUI GameObject so that it
/// inherits the correct TextMeshProUGUI component and the game's own font/material.
///
/// IMPORTANT: This MonoBehaviour must remain on an ACTIVE GameObject at all
/// times so that Update() continues to run. Visibility is controlled via
/// <c>tmpText.enabled</c> — never via <c>gameObject.SetActive(false)</c>,
/// which would stop Update() from executing.
/// </summary>
internal sealed class MorePeakHUD : MonoBehaviour {
	// The TextMeshProUGUI component inherited from the cloned AscentUI object.
	private TMP_Text tmpText;

	// Cache the last state so we only update the TMP mesh when content changes,
	// avoiding unnecessary dirty-marks every frame.
	private string lastDisplayedText = null;
	private bool lastEnabledState = false;

	// Track whether we've emitted the first-show INFO log.
	private bool hasLoggedFirstDisplay = false;

	// =========================================================================
	// Unity lifecycle
	// =========================================================================

	private void Start() {
		tmpText = GetComponent<TMP_Text>();

		if (tmpText == null) {
			MorePeakPlugin.ModLogger.LogError(
				"[MorePeak] MorePeakHUD: no TMP_Text component found on its GameObject. " +
				"The HUD will be non-functional. This likely means the AscentUI clone did " +
				"not carry a TextMeshProUGUI component — check whether AscentUI's layout changed.");
			// Disable this script (NOT the gameobject) so Update() stops running.
			enabled = false;
			return;
		}

		// Style the text to match the game's UI aesthetic.
		tmpText.fontSize = 24f;
		tmpText.alignment = TextAlignmentOptions.TopRight;
		tmpText.outlineColor = new Color32(0, 0, 0, byte.MaxValue);
		tmpText.outlineWidth = 0.055f;
		tmpText.autoSizeTextContainer = true;

		// Hide the TMP component initially — NOT the gameobject.
		// Deactivating the gameobject here would stop Update() from ever running.
		tmpText.enabled = false;
		lastEnabledState = false;

		MorePeakPlugin.ModLogger.LogInfo(
			$"[MorePeak] MorePeakHUD ready on '{gameObject.name}' " +
			$"(ShowCurrentLevel={MorePeakPlugin.showCurrentLevelGUIConfig.Value}, " +
			$"ShowConfig={MorePeakPlugin.showSelectedLevelConfigGUIConfig.Value}).");
		MorePeakPlugin.LogVerbose($"MorePeakHUD.Start complete — TMP_Text found and styled.");
	}

	private void Update() {
		if (tmpText == null) return;

		try {
			// Build the list of lines to display this frame.
			var displayLines = new List<string>(2);

			if (MorePeakPlugin.showCurrentLevelGUIConfig.Value &&
				!string.IsNullOrEmpty(MorePeakPlugin.currentLevelName)) {
				displayLines.Add("Current Level: " + MorePeakPlugin.currentLevelName);
			}

			if (MorePeakPlugin.showSelectedLevelConfigGUIConfig.Value) {
				displayLines.Add("Config: " + MorePeakPlugin.selectedLevelConfig.Value);
			}

			bool shouldBeVisible = displayLines.Count > 0;
			string newText = shouldBeVisible ? string.Join("\n", displayLines) : string.Empty;

			// Only toggle TMP visibility when the state actually changes to avoid
			// triggering unnecessary canvas redraws.
			if (shouldBeVisible != lastEnabledState) {
				tmpText.enabled = shouldBeVisible;
				lastEnabledState = shouldBeVisible;
				MorePeakPlugin.LogVerbose(
					$"MorePeakHUD visibility changed to {shouldBeVisible} " +
					$"(currentLevel='{MorePeakPlugin.currentLevelName}').");
			}

			// Only assign to tmpText.text when the content has actually changed,
			// to avoid rebuilding the TMP mesh every single frame.
			if (shouldBeVisible && newText != lastDisplayedText) {
				tmpText.text = newText;
				lastDisplayedText = newText;

				// Emit an INFO log the very first time the display shows content so it
				// is easy to confirm in the log that the HUD is actively rendering.
				if (!hasLoggedFirstDisplay) {
					MorePeakPlugin.ModLogger.LogInfo(
						$"[MorePeak] HUD displaying: \"{newText}\"");
					hasLoggedFirstDisplay = true;
				} else {
					MorePeakPlugin.LogVerbose($"MorePeakHUD text updated to: \"{newText}\".");
				}
			}

			// When content disappears, reset the first-display guard so we log again
			// the next time the HUD comes back on screen (e.g. next round).
			if (!shouldBeVisible && hasLoggedFirstDisplay) {
				hasLoggedFirstDisplay = false;
				lastDisplayedText = null;
				MorePeakPlugin.LogVerbose("MorePeakHUD content cleared (no lines to display).");
			}
		} catch (System.Exception ex) {
			MorePeakPlugin.ModLogger.LogError($"[MorePeak] Exception in MorePeakHUD.Update: {ex}");
		}
	}
}
