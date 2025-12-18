using System;
using UnityEngine;

public class PositionYBind : MonoBehaviour {
    [SerializeField] Transform target;

    void Update() {
        transform.position = new Vector3(transform.position.x, target.position.y, transform.position.z);
    }
}