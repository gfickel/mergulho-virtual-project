using UnityEngine;
using UnityEngine.InputSystem;

public class ObjectInteraction : MonoBehaviour
{
    public GameObject infoText;
    public string targetName = "Tubarão Martelo";

    void Update()
    {
        if (Pointer.current == null)
            return;

        if (Pointer.current.press.wasPressedThisFrame)
        {
            Vector2 position = Pointer.current.position.ReadValue();
            HandleInput(position);
        }
    }

    void HandleInput(Vector2 position)
    {
        Debug.Log($"Tubarão Martelo HandleInput at position: {position}");

        if (Camera.main == null)
        {
            Debug.LogError("No Main Camera found! Ensure your active camera is tagged 'MainCamera'.");
            return;
        }

        Ray ray = Camera.main.ScreenPointToRay(position);
        
        // Visual debugging in Scene view
        Debug.DrawRay(ray.origin, ray.direction * 100, Color.red, 2.0f);
        Debug.Log($"Ray Origin: {ray.origin}, Direction: {ray.direction}");

        RaycastHit hit;
        // Search for objects within a 0.2f radius of the ray to make touching easier
        if (Physics.SphereCast(ray, 0.2f, out hit))
        {
            Debug.Log("Hit object: " + hit.collider.gameObject.name);
            if (hit.collider.gameObject.name == targetName)
            {
                if (infoText != null)
                {
                    infoText.SetActive(true);
                }
                else
                {
                    Debug.LogWarning("InfoText GameObject is not assigned in ObjectInteraction script.");
                }
            }
        }
        else
        {
            if (infoText != null)
            {
                infoText.SetActive(false);
            }
            else
            {
                Debug.LogWarning("InfoText GameObject is not assigned in ObjectInteraction script.");
            }
        }
    }
}
