using UnityEngine;

public abstract class BaseUI : MonoBehaviour {
    public virtual void Open(params object[] args) {
        gameObject.SetActive(true);
    }
    
    public virtual void Close() {
        gameObject.SetActive(false);
    }
}