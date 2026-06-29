using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class InputFieldMovementBlocker : MonoBehaviour
{
    [Tooltip("Trascina qui il GameObject 'Locomotion' dalla gerarchia")]
    public GameObject locomotionObject;

    private TMP_InputField inputField;
    private bool locomotionWasActive;
    private bool isBlockingMovement;
    private readonly List<Behaviour> disabledMovementBehaviours = new List<Behaviour>();

    private void Awake()
    {
        inputField = GetComponent<TMP_InputField>();

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

    private bool isActivating;

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
            if (Input.GetKeyDown(KeyCode.T))
            {
                StartCoroutine(ActivateInputFieldDeferred());
            }
        }
        else if (inputField.isFocused)
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                inputField.DeactivateInputField();
                if (UnityEngine.EventSystems.EventSystem.current != null)
                {
                    UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
                }
            }
        }
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

        var eventSystem = UnityEngine.EventSystems.EventSystem.current;
        return eventSystem != null && eventSystem.currentSelectedGameObject == gameObject;
    }

    private void BlockMovement()
    {
        SetLocomotionActive(false);
        SetXrMovementProvidersActive(false);
#if UNITY_INPUT_SYSTEM
        SetPlayerInputActive(false);
        SetUISubmitActive(false);
#endif
    }

    private void RestoreMovement()
    {
        RestoreLocomotion();
        SetXrMovementProvidersActive(true);
#if UNITY_INPUT_SYSTEM
        SetPlayerInputActive(true);
        SetUISubmitActive(true);
#endif
    }

    private void SetLocomotionActive(bool isActive)
    {
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

    private void SetXrMovementProvidersActive(bool isActive)
    {
        if (!isActive)
        {
            if (disabledMovementBehaviours.Count > 0)
                return;

            foreach (var behaviour in FindMovementBehaviours())
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

    private static Behaviour[] FindMovementBehaviours()
    {
#if UNITY_2023_1_OR_NEWER
        var behaviours = FindObjectsByType<Behaviour>(FindObjectsInactive.Include);
#else
        var behaviours = FindObjectsOfType<Behaviour>(true);
#endif
        var movementBehaviours = new List<Behaviour>();

        foreach (var behaviour in behaviours)
        {
            if (IsXrMovementBehaviour(behaviour))
                movementBehaviours.Add(behaviour);
        }

        return movementBehaviours.ToArray();
    }

    private static bool IsXrMovementBehaviour(Behaviour behaviour)
    {
        var typeName = behaviour.GetType().Name;
        return typeName.Contains("MoveProvider") ||
               typeName.Contains("TurnProvider") ||
               typeName.Contains("LocomotionProvider") ||
               typeName.Contains("TeleportationProvider");
    }

#if UNITY_INPUT_SYSTEM
    private UnityEngine.InputSystem.PlayerInput cachedPlayerInput;

    private void SetPlayerInputActive(bool active)
    {
        if (!active)
        {
            if (cachedPlayerInput != null)
                return;

#if UNITY_2023_1_OR_NEWER
            cachedPlayerInput = FindFirstObjectByType<UnityEngine.InputSystem.PlayerInput>();
#else
            cachedPlayerInput = FindObjectOfType<UnityEngine.InputSystem.PlayerInput>();
#endif
            if (cachedPlayerInput != null)
            {
                cachedPlayerInput.enabled = false;
            }
        }
        else
        {
            if (cachedPlayerInput != null)
            {
                cachedPlayerInput.enabled = true;
                cachedPlayerInput = null;
            }
        }
    }

    private void SetUISubmitActive(bool active)
    {
        var eventSystem = UnityEngine.EventSystems.EventSystem.current;
        if (eventSystem == null) return;

        var inputModule = eventSystem.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        if (inputModule == null) return;

        var submitAction = inputModule.submit;
        if (submitAction != null && submitAction.action != null)
        {
            if (active)
                submitAction.action.Enable();
            else
                submitAction.action.Disable();
        }
    }
#endif
}
