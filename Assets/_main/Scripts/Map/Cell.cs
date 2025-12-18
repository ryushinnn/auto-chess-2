using System;
using Sirenix.OdinInspector;
using UnityEngine;

public abstract class Cell : MonoBehaviour {
    [SerializeField] MeshRenderer meshRenderer;
    [SerializeField] Material normalMat;
    [SerializeField] Material occupiedMat;
    [SerializeField] Material highlightMat;
    [SerializeField] Material notAvailableMat;

    [SerializeField, ReadOnly] bool occupied;
    [SerializeField, ReadOnly] bool highlight;
    [SerializeField, ReadOnly] bool notAvailable;
    
    // blue mark. lowest priority
    public void SetOccupied(bool occupied) {
        this.occupied = occupied;
        UpdateMaterial();
    }
    
    // green mark. medium priority
    public void SetHighlight(bool highlight) {
        this.highlight = highlight;
        UpdateMaterial();
    }
    
    // red mark. highest priority
    public void SetNotAvailable(bool notAvailable) {
        this.notAvailable = notAvailable;
        UpdateMaterial();
    }
    
    void UpdateMaterial() {
        meshRenderer.sharedMaterial = notAvailable ? notAvailableMat 
            : highlight ? highlightMat 
            : occupied ? occupiedMat 
            : normalMat;
    }
}