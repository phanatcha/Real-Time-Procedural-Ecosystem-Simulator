using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// Free-flying observer camera. WASD moves, Q/E lowers and raises, holding the right mouse button
// looks around, Shift moves faster and the scroll wheel changes speed. It runs on unscaled time,
// so it keeps working while the simulation is paused or fast-forwarded.
[DisallowMultipleComponent]
public class GodCamera : MonoBehaviour
{
    [Header("Movement")]
    [Min(0.1f)] public float moveSpeed = 60f;
    [Min(1f)] public float fastMultiplier = 4f;
    [Tooltip("Each scroll step multiplies or divides the move speed by this amount.")]
    [Range(1.01f, 2f)] public float scrollSpeedStep = 1.2f;
    [Min(0.1f)] public float minimumMoveSpeed = 5f;
    [Min(0.1f)] public float maximumMoveSpeed = 1000f;

    [Header("Looking")]
    [Tooltip("Degrees turned per pixel of mouse movement while the right mouse button is held.")]
    [Min(0f)] public float lookSensitivity = 0.15f;

    [Header("Terrain")]
    [Tooltip("Optional. Found automatically when left empty.")]
    public TerrainGenerator terrainGenerator;
    [Tooltip("Load terrain around this camera instead of the scene's original viewer, " +
             "so terrain appears wherever you fly.")]
    public bool loadTerrainAroundCamera = true;
    [Tooltip("Stops the camera sinking into the ground. Zero disables the check.")]
    [Min(0f)] public float minimumHeightAboveGround = 2f;
    [Tooltip("Keeps the camera above the water surface, which has no underwater view. Zero disables the check.")]
    [Min(0f)] public float minimumHeightAboveWater = 1.5f;

    float yaw;
    float pitch;
    bool isLooking;

    void Start()
    {
        Vector3 angles = transform.eulerAngles;
        yaw = angles.y;
        pitch = Mathf.DeltaAngle(0f, angles.x);

        if (terrainGenerator == null) terrainGenerator = FindAnyObjectByType<TerrainGenerator>();
        if (loadTerrainAroundCamera && terrainGenerator != null) terrainGenerator.viewer = transform;
    }

    void OnDisable()
    {
        SetLooking(false);
    }

    void Update()
    {
        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            Look(mouse);
            AdjustSpeed(mouse);
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && !IsTypingInUI())
        {
            Move(keyboard, Time.unscaledDeltaTime);
        }

        KeepAboveGround();
    }

    void Look(Mouse mouse)
    {
        SetLooking(mouse.rightButton.isPressed);
        if (!isLooking)
        {
            return;
        }

        Vector2 delta = mouse.delta.ReadValue() * lookSensitivity;
        yaw += delta.x;
        pitch = Mathf.Clamp(pitch - delta.y, -89f, 89f);
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    void AdjustSpeed(Mouse mouse)
    {
        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) < 0.01f)
        {
            return;
        }

        float step = scroll > 0f ? scrollSpeedStep : 1f / scrollSpeedStep;
        moveSpeed = Mathf.Clamp(moveSpeed * step, minimumMoveSpeed, Mathf.Max(minimumMoveSpeed, maximumMoveSpeed));
    }

    void Move(Keyboard keyboard, float deltaTime)
    {
        Vector3 input = Vector3.zero;
        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) input.z += 1f;
        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) input.z -= 1f;
        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) input.x += 1f;
        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) input.x -= 1f;
        if (keyboard.eKey.isPressed) input.y += 1f;
        if (keyboard.qKey.isPressed) input.y -= 1f;

        if (input == Vector3.zero)
        {
            return;
        }

        float speed = moveSpeed * (keyboard.shiftKey.isPressed ? fastMultiplier : 1f);

        // Forward follows where the camera looks; Q/E always move straight down and up.
        Vector3 direction = transform.forward * input.z + transform.right * input.x + Vector3.up * input.y;
        transform.position += Vector3.ClampMagnitude(direction, 1f) * speed * deltaTime;
    }

    void KeepAboveGround()
    {
        if (terrainGenerator == null)
        {
            return;
        }

        float minimumY = float.NegativeInfinity;

        // Cached-only sampling never generates terrain on the main thread.
        TerrainEnvironmentSampler sampler = minimumHeightAboveGround > 0f ? terrainGenerator.WorldEnvironmentSampler : null;
        if (sampler != null && sampler.TrySampleCached(transform.position, out EnvironmentSample ground))
        {
            minimumY = ground.height + minimumHeightAboveGround;
        }

        if (minimumHeightAboveWater > 0f && terrainGenerator.HasWaterSurface)
        {
            minimumY = Mathf.Max(minimumY, terrainGenerator.WaterLevelHeight + minimumHeightAboveWater);
        }

        if (transform.position.y < minimumY)
        {
            Vector3 position = transform.position;
            position.y = minimumY;
            transform.position = position;
        }
    }

    void SetLooking(bool looking)
    {
        if (isLooking == looking)
        {
            return;
        }

        isLooking = looking;
        Cursor.lockState = looking ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !looking;
    }

    // True while a text field such as the seed input has keyboard focus, so shortcuts should be ignored.
    public static bool IsTypingInUI()
    {
        GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (selected == null)
        {
            return false;
        }

        TMP_InputField textMeshField = selected.GetComponent<TMP_InputField>();
        if (textMeshField != null && textMeshField.isFocused)
        {
            return true;
        }

        UnityEngine.UI.InputField legacyField = selected.GetComponent<UnityEngine.UI.InputField>();
        return legacyField != null && legacyField.isFocused;
    }
}
