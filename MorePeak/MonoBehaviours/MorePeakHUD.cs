using System.Collections.Generic;
using UnityEngine;
using TMPro;

namespace MorePeak.MonoBehaviours;

internal sealed class MorePeakHUD : MonoBehaviour {
	private TMP_Text tmpText;

	private void Start() {
		tmpText = GetComponent<TMP_Text>();
		tmpText.fontSize = 24f;
		tmpText.alignment = TextAlignmentOptions.TopRight;
		tmpText.outlineColor = new Color32(0, 0, 0, byte.MaxValue);
		tmpText.outlineWidth = 0.055f;
		tmpText.autoSizeTextContainer = true;
	}

	private void Update() {
		if (tmpText != null) {
			List<string> displayLines = [];

			if (MorePeakPlugin.showCurrentLevelGUIConfig.Value) {
				displayLines.Add("Current Level: " + MorePeakPlugin.currentLevelName);
			}

			if (MorePeakPlugin.showSelectedLevelConfigGUIConfig.Value) {
				displayLines.Add("Config: " + MorePeakPlugin.selectedLevelConfig.Value);
			}

			// Show/hide based on whether we have content
			if (displayLines.Count > 0) {
				tmpText.gameObject.SetActive(true);
				tmpText.text = string.Join("\n", displayLines);
			} else {
				tmpText.gameObject.SetActive(false);
			}
		}
	}
}
