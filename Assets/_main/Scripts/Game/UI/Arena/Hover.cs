using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AutoChess.Game {
    /// <summary>Pointer enter / exit callbacks on a UI element (tooltips); the exit also fires when it gets hidden.</summary>
    public sealed class Hover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler {
        public Action Enter;
        public Action Exit;
        bool inside;

        public static Hover On(Component target, Action enter, Action exit = null) {
            var hover = target.gameObject.GetComponent<Hover>() ?? target.gameObject.AddComponent<Hover>();
            hover.Enter = enter;
            hover.Exit = exit ?? Tooltip.Hide;
            return hover;
        }

        public void OnPointerEnter(PointerEventData eventData) {
            if (eventData.dragging) return;
            inside = true;
            Enter?.Invoke();
        }

        public void OnPointerExit(PointerEventData eventData) {
            if (!inside) return;
            inside = false;
            Exit?.Invoke();
        }

        void OnDisable() {
            if (!inside) return;
            inside = false;
            Exit?.Invoke();
        }
    }
}
