using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DefaultExecutionOrder(-10000)]
public class InputFieldMovementBlocker : MonoBehaviour
{
    [Tooltip("Opzionale: usa il GameObject 'Move'. Se vuoto, viene cercato automaticamente.")]
    public GameObject locomotionObject;

    [Tooltip("Tasto per attivare il campo di testo")]
    public KeyCode activateKey = KeyCode.T;

    private TMP_InputField inputField;
    private bool locomotionWasActive;
    private bool isBlockingMovement;
    private readonly List<Behaviour> disabledMovementBehaviours = new List<Behaviour>();
#if ENABLE_INPUT_SYSTEM
    private readonly List<InputActionMap> disabledActionMaps = new List<InputActionMap>();
#endif
    private bool isActivating;

    private void Awake()
    {
        inputField = GetComponent<TMP_InputField>();
        FindLocomotionObjectIfNeeded();

        if (inputField != null)
        {
            inputField.onSelect.AddListener(OnSelected);
            inputField.onDeselect.AddListener(OnDeselected);
            inputField.onEndEdit.AddListener(OnDeselected);
        }
        else
        {
            Debug.LogWarning($"{nameof(InputFieldMovementBlocker)} richiede un TMP_InputField sullo stesso GameObject.", this);
        }
    }

    private void Update()
    {
        if (inputField == null)
            return;

        if (IsInputFieldActive())
            BlockMovement();
        else
            RestoreMovement();

        if (!inputField.isFocused && !isActivating)
        {
            if (WasKeyPressed(activateKey))
            {
                StartCoroutine(ActivateInputFieldDeferred());
            }
        }
        else if (inputField.isFocused)
        {
            if (WasKeyPressed(KeyCode.Escape))
            {
                inputField.DeactivateInputField();
                if (EventSystem.current != null)
                {
                    EventSystem.current.SetSelectedGameObject(null);
                }
            }
        }
    }

    private static bool WasKeyPressed(KeyCode key)
    {
#if ENABLE_INPUT_SYSTEM
        var keyboard = Keyboard.current;
        if (keyboard == null)
            return false;

        switch (key)
        {
            case KeyCode.T:
                return keyboard.tKey.wasPressedThisFrame;
            case KeyCode.Escape:
                return keyboard.escapeKey.wasPressedThisFrame;
            default:
                return false;
        }
#else
        return Input.GetKeyDown(key);
#endif
    }

    private IEnumerator ActivateInputFieldDeferred()
    {
        isActivating = true;
        yield return null;
        if (inputField != null)
        {
            inputField.Select();
            inputField.ActivateInputField();
        }
        isActivating = false;
    }

    private void OnSelected(string value)
    {
        BlockMovement();
    }

    private void OnDeselected(string value)
    {
        RestoreMovement();
    }

    private void OnDisable()
    {
        RestoreMovement();
    }

    private bool IsInputFieldActive()
    {
        if (inputField.isFocused)
            return true;

        var eventSystem = EventSystem.current;
        return eventSystem != null && eventSystem.currentSelectedGameObject == gameObject;
    }

    private void BlockMovement()
    {
        SetLocomotionActive(false);
        SetInputBlockingBehavioursActive(false);
#if ENABLE_INPUT_SYSTEM
        SetGameplayActionMapsActive(false);
#endif
    }

    private void RestoreMovement()
    {
        RestoreLocomotion();
        SetInputBlockingBehavioursActive(true);
#if ENABLE_INPUT_SYSTEM
        SetGameplayActionMapsActive(true);
#endif
    }

    private void SetLocomotionActive(bool isActive)
    {
        FindLocomotionObjectIfNeeded();

        if (locomotionObject == null)
            return;

        if (!isActive && !isBlockingMovement)
        {
            locomotionWasActive = locomotionObject.activeSelf;
            isBlockingMovement = true;
        }

        if (locomotionObject.activeSelf != isActive)
            locomotionObject.SetActive(isActive);
    }

    private void RestoreLocomotion()
    {
        if (!isBlockingMovement || locomotionObject == null)
            return;

        if (locomotionObject.activeSelf != locomotionWasActive)
            locomotionObject.SetActive(locomotionWasActive);

        isBlockingMovement = false;
    }

    private void FindLocomotionObjectIfNeeded()
    {
        if (locomotionObject != null && locomotionObject.name == "Move")
            return;

#if UNITY_2023_1_OR_NEWER
        var transforms = FindObjectsByType<Transform>(FindObjectsInactive.Include);
#else
        var transforms = FindObjectsOfType<Transform>(true);
#endif

        foreach (var currentTransform in transforms)
        {
            if (currentTransform.name == "Move")
            {
                locomotionObject = currentTransform.gameObject;
                return;
            }
        }

        if (locomotionObject != null)
            return;

        foreach (var currentTransform in transforms)
        {
            if (currentTransform.name == "Locomotion")
            {
                locomotionObject = currentTransform.gameObject;
                return;
            }
        }
    }

    private void SetInputBlockingBehavioursActive(bool isActive)
    {
        if (!isActive)
        {
            if (disabledMovementBehaviours.Count > 0)
                return;

            foreach (var behaviour in FindInputBlockingBehaviours())
            {
                if (behaviour == null || behaviour == this || !behaviour.enabled)
                    continue;

                behaviour.enabled = false;
                disabledMovementBehaviours.Add(behaviour);
            }

            return;
        }

        for (int i = 0; i < disabledMovementBehaviours.Count; i++)
        {
            if (disabledMovementBehaviours[i] != null)
                disabledMovementBehaviours[i].enabled = true;
        }

        disabledMovementBehaviours.Clear();
    }

#if ENABLE_INPUT_SYSTEM
    private void SetGameplayActionMapsActive(bool isActive)
    {
        if (!isActive)
        {
            if (disabledActionMaps.Count > 0)
                return;

            var actionAssets = Resources.FindObjectsOfTypeAll<InputActionAsset>();
            foreach (var actionAsset in actionAssets)
            {
                if (actionAsset == null)
                    continue;

                foreach (var actionMap in actionAsset.actionMaps)
                {
                    if (actionMap == null || !actionMap.enabled || IsUiActionMap(actionMap))
                        continue;

                    actionMap.Disable();
                    disabledActionMaps.Add(actionMap);
                }
            }

            return;
        }

        for (int i = 0; i < disabledActionMaps.Count; i++)
        {
            if (disabledActionMaps[i] != null)
                disabledActionMaps[i].Enable();
        }

        disabledActionMaps.Clear();
    }

    private static bool IsUiActionMap(InputActionMap actionMap)
    {
        var mapName = actionMap.name.ToLowerInvariant();
        return mapName == "ui" || mapName.Contains("ui");
    }
#endif

    private static Behaviour[] FindInputBlockingBehaviours()
    {
#if UNITY_2023_1_OR_NEWER
        var behaviours = FindObjectsByType<Behaviour>(FindObjectsInactive.Include);
#else
        var behaviours = FindObjectsOfType<Behaviour>(true);
#endif
        var movementBehaviours = new List<Behaviour>();

        foreach (var behaviour in behaviours)
        {
            if (ShouldDisableWhileTyping(behaviour))
                movementBehaviours.Add(behaviour);
        }

        return movementBehaviours.ToArray();
    }

    private static bool ShouldDisableWhileTyping(Behaviour behaviour)
    {
        var typeName = behaviour.GetType().Name;
        return typeName.Contains("MoveProvider") ||
               typeName.Contains("TurnProvider") ||
               typeName.Contains("LocomotionProvider") ||
               typeName.Contains("TeleportationProvider") ||
               typeName.Contains("XRDeviceSimulator") ||
               typeName.Contains("DeviceSimulator");
    }
}
