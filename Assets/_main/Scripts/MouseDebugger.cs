using System;
using UnityEngine;

public class MouseDebugger : MonoBehaviour {
    void Update() {
        if (Input.GetMouseButtonDown(0)) {
            // Get the mouse position in screen coordinates
            Vector3 mousePosition = Input.mousePosition;
            
            // Convert the screen coordinates to a ray
            Ray ray = Camera.main.ScreenPointToRay(mousePosition);
            
            // Perform a raycast using the ray
            if (Physics.Raycast(ray, out RaycastHit hit)) {
                // Log the hit information
                Debug.Log("Hit object: " + hit.collider.gameObject.name);
            }
        }
    }
}