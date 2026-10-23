using System;
using UnityEngine;

namespace AutoChess.Game {
    /// <summary>A visual piece of a <see cref="UnitView"/> (was HeroAbility).</summary>
    public abstract class UnitPart : MonoBehaviour {
        [NonSerialized] protected UnitView unit;

        public virtual void Initialize(UnitView owner) {
            unit = owner;
        }

        /// <param name="home">The unit belongs to the local player's side.</param>
        public virtual void ResetAll(bool home) { }
    }
}
