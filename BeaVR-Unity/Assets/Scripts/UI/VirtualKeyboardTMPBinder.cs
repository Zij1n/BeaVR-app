using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class VirtualKeyboardTMPBinder : MonoBehaviour
{
    [SerializeField] private OVRVirtualKeyboard keyboard;
    [SerializeField] private TMP_InputField targetField;
    [SerializeField] private bool useSystemKeyboardFallback = true;
    [SerializeField] private string placeholderText = "Server IP";

    private TouchScreenKeyboard _systemKeyboard;
    private bool _subscribedToFieldEvents;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void InstallRuntimeBinders()
    {
        var fieldManagers = FindObjectsByType<IPFieldManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var fieldManager in fieldManagers)
        {
            if (fieldManager == null || fieldManager.ipInput == null)
            {
                continue;
            }

            var binder = fieldManager.ipInput.GetComponent<VirtualKeyboardTMPBinder>();
            if (binder == null)
            {
                binder = fieldManager.ipInput.gameObject.AddComponent<VirtualKeyboardTMPBinder>();
            }

            binder.SetTargetField(fieldManager.ipInput);
        }
    }

    void Awake()
    {
        if (targetField == null)
        {
            targetField = GetComponent<TMP_InputField>();
        }

        ApplyInputFieldSettings();
    }

    void OnEnable()
    {
        RegisterFieldEvents();
        RegisterOVRKeyboardEvents();
    }

    void OnDisable()
    {
        UnregisterFieldEvents();
        UnregisterOVRKeyboardEvents();
        CloseKeyboard();
    }

    void Update()
    {
        if (_systemKeyboard == null || targetField == null)
        {
            return;
        }

        if (!string.Equals(targetField.text, _systemKeyboard.text))
        {
            targetField.SetTextWithoutNotify(_systemKeyboard.text);
            targetField.caretPosition = targetField.text.Length;
        }

        switch (_systemKeyboard.status)
        {
            case TouchScreenKeyboard.Status.Done:
            case TouchScreenKeyboard.Status.Canceled:
            case TouchScreenKeyboard.Status.LostFocus:
                FinalizeKeyboardEntry();
                break;
        }
    }

    public void SetTargetField(TMP_InputField inputField)
    {
        if (targetField == inputField)
        {
            return;
        }

        UnregisterFieldEvents();
        targetField = inputField;
        ApplyInputFieldSettings();
        RegisterFieldEvents();
    }

    public void OpenKeyboard()
    {
        if (targetField == null)
        {
            return;
        }

        ApplyInputFieldSettings();

        if (keyboard != null)
        {
            keyboard.gameObject.SetActive(true);
            return;
        }

        if (!useSystemKeyboardFallback || !TouchScreenKeyboard.isSupported)
        {
            return;
        }

        if (_systemKeyboard != null)
        {
            _systemKeyboard.active = false;
        }

        _systemKeyboard = TouchScreenKeyboard.Open(
            targetField.text,
            TouchScreenKeyboardType.Default,
            false,
            false,
            false,
            false,
            placeholderText);

        if (_systemKeyboard != null)
        {
            _systemKeyboard.text = targetField.text;
        }
    }

    public void CloseKeyboard()
    {
        if (keyboard != null)
        {
            keyboard.gameObject.SetActive(false);
        }

        if (_systemKeyboard != null)
        {
            _systemKeyboard.active = false;
            _systemKeyboard = null;
        }
    }

    void ApplyInputFieldSettings()
    {
        if (targetField == null)
        {
            return;
        }

        targetField.shouldHideSoftKeyboard = false;
        targetField.resetOnDeActivation = false;
    }

    void RegisterFieldEvents()
    {
        if (_subscribedToFieldEvents || targetField == null)
        {
            return;
        }

        targetField.onSelect.AddListener(HandleFieldSelected);
        targetField.onDeselect.AddListener(HandleFieldDeselected);
        _subscribedToFieldEvents = true;
    }

    void UnregisterFieldEvents()
    {
        if (!_subscribedToFieldEvents || targetField == null)
        {
            return;
        }

        targetField.onSelect.RemoveListener(HandleFieldSelected);
        targetField.onDeselect.RemoveListener(HandleFieldDeselected);
        _subscribedToFieldEvents = false;
    }

    void RegisterOVRKeyboardEvents()
    {
        if (keyboard == null)
        {
            return;
        }

        keyboard.CommitTextEvent.AddListener(HandleCommittedText);
        keyboard.BackspaceEvent.AddListener(HandleBackspace);
    }

    void UnregisterOVRKeyboardEvents()
    {
        if (keyboard == null)
        {
            return;
        }

        keyboard.CommitTextEvent.RemoveListener(HandleCommittedText);
        keyboard.BackspaceEvent.RemoveListener(HandleBackspace);
    }

    void HandleFieldSelected(string _)
    {
        OpenKeyboard();
    }

    void HandleFieldDeselected(string _)
    {
        if (_systemKeyboard == null)
        {
            CloseKeyboard();
        }
    }

    void HandleCommittedText(string committedText)
    {
        if (targetField == null || string.IsNullOrEmpty(committedText))
        {
            return;
        }

        targetField.text += committedText;
        targetField.caretPosition = targetField.text.Length;
    }

    void HandleBackspace()
    {
        if (targetField == null || string.IsNullOrEmpty(targetField.text))
        {
            return;
        }

        targetField.text = targetField.text.Substring(0, targetField.text.Length - 1);
        targetField.caretPosition = targetField.text.Length;
    }

    void FinalizeKeyboardEntry()
    {
        if (targetField != null)
        {
            targetField.DeactivateInputField();
            EventSystem.current?.SetSelectedGameObject(null);
        }

        CloseKeyboard();
    }
}
