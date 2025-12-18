using System;
using UnityEngine;

public class Billboard : MonoBehaviour {
    Camera cam;
    
    void Awake() {
        cam = Camera.main;
    }

    void Update() {
        transform.LookAt(transform.position + cam.transform.rotation * Vector3.forward, cam.transform.rotation * Vector3.up);
    }
}