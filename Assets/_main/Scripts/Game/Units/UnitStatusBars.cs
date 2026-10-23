using AutoChess.Core;
using DG.Tweening;
using UnityEngine;

namespace AutoChess.Game {
    /// <summary>Health / energy bars and floating numbers (the visual half of the old HeroAttributes).</summary>
    public class UnitStatusBars : UnitPart {
        [SerializeField] ProgressBar healthBar;
        [SerializeField] ProgressBar energyBar;
        [SerializeField] Transform hpTextParent;
        [SerializeField] Color allyHpColor, enemyHpColor;
        [SerializeField] Color normalEnergyColor, drainingEnergyColor;

        Sequence drainSequence;
        float shownHealth = -1;
        float shownEnergy = -1;

        public bool IsDraining => drainSequence != null && drainSequence.IsActive();

        public override void ResetAll(bool home) {
            drainSequence?.Kill();
            drainSequence = null;
            unit.SwitchCanvas(true);
            healthBar.UpdateAmount(1, true);
            energyBar.UpdateAmount(0, true);
            shownHealth = 1;
            shownEnergy = 0;
            healthBar.SetMainColor(home ? allyHpColor : enemyHpColor);
            energyBar.SetMainColor(normalEnergyColor);
        }

        public void SetHealth(float fraction) {
            if (Mathf.Approximately(fraction, shownHealth)) return;
            shownHealth = fraction;
            healthBar.UpdateAmount(fraction);
        }

        public void SetEnergy(float fraction) {
            if (IsDraining || Mathf.Approximately(fraction, shownEnergy)) return;
            shownEnergy = fraction;
            energyBar.UpdateAmount(fraction);
        }

        /// <summary>The energy bar empties from full over <paramref name="duration"/> after <paramref name="delay"/>.</summary>
        public void PlayEnergyDrain(float delay, float duration) {
            energyBar.SetMainColor(drainingEnergyColor);
            drainSequence?.Kill();
            drainSequence = DOTween.Sequence()
                .AppendInterval(delay)
                .Append(DOVirtual.Float(1, 0, duration, energyBar.UpdateAmount).SetEase(Ease.Linear))
                .AppendCallback(() => {
                    energyBar.SetMainColor(normalEnergyColor);
                    shownEnergy = 0;
                    drainSequence = null;
                });
        }

        public void ShowDamage(float amount, DamageType type, bool crit, int stackIndex) {
            HpTextSpawner.Instance.SpawnDamage(hpTextParent, amount, type, crit, stackIndex);
        }

        public void ShowHeal(float amount) {
            HpTextSpawner.Instance.SpawnHeal(hpTextParent, amount);
        }

        void OnDisable() {
            drainSequence?.Kill();
            drainSequence = null;
        }
    }
}
