using UnityEngine;
using TMPro;

public class SaveAndReturnIP : MonoBehaviour
{
    public TMP_InputField ipInput;
    public CanvasSwitch canvasSwitch;
    public ToastManager toastManager;

    public const string PlayerPrefsKey = "ServerIP";

    private void Start()
    {
        string saved = PlayerPrefs.GetString(PlayerPrefsKey, string.Empty);
        if (!string.IsNullOrEmpty(saved))
        {
            if (ipInput != null) ipInput.SetTextWithoutNotify(saved);
        }
    }

    public void OnClick_SaveAndReturn()
    {
        // Prefer last validated value from IPFieldManager
        string normalized = IPFieldManager.GetLastValidatedIPv4();
        if (string.IsNullOrEmpty(normalized) && ipInput != null)
        {
            normalized = IPFieldManager.NormalizeIPv4Input(ipInput.text);
        }

        if (!IPFieldManager.IsValidIPv4(normalized))
        {
            ResolveToastManager()?.Error("Enter a valid IPv4 address.");
            Debug.Log("[SaveAndReturnIP] No validated IP available.");
            return;
        }

        PlayerPrefs.SetString(PlayerPrefsKey, normalized);
        PlayerPrefs.Save();

        if (ipInput != null)
        {
            ipInput.SetTextWithoutNotify(normalized);
        }

        Debug.Log($"[SaveAndReturnIP] Saved: {normalized}");
        ResolveToastManager()?.Success($"IP set to {normalized}");

        var switcher = canvasSwitch != null ? canvasSwitch : GetComponent<CanvasSwitch>();
        if (switcher != null) switcher.Switch();

        // Clear ephemeral stash after successful save
        IPFieldManager.ClearLastValidatedIPv4();
    }

    // Read saved IP anywhere when needed: PlayerPrefs.GetString(PlayerPrefsKey, "")

    private ToastManager ResolveToastManager()
    {
        if (toastManager != null)
        {
            return toastManager;
        }

        if (ipInput != null)
        {
            var fieldManager = ipInput.GetComponent<IPFieldManager>();
            if (fieldManager != null && fieldManager.toastManager != null)
            {
                toastManager = fieldManager.toastManager;
                return toastManager;
            }
        }

        toastManager = FindFirstObjectByType<ToastManager>();
        return toastManager;
    }
}

