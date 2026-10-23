using System.Collections;
using DG.Tweening;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Serialization;

namespace AutoChess.Game {
    /// <summary>Animator driver of a hero model: locomotion states plus attack / skill / dive interactions.</summary>
    public abstract class Mecanim : MonoBehaviour {
        protected enum State {
            None = 0,
            Idle = 1,
            Run = 2,
            Death = 99
        }

        protected enum Interaction {
            None = 0,
            Dive = 1,
            Attack = 2,
            Skill = 3
        }

        protected static readonly int paramChangeState = Animator.StringToHash("change_state");
        protected static readonly int paramState = Animator.StringToHash("state");
        protected static readonly int paramLastState = Animator.StringToHash("last_state");
        protected static readonly int paramIdleIn = Animator.StringToHash("idle_in");
        protected static readonly int paramRunIn = Animator.StringToHash("run_in");
        protected static readonly int paramInteract = Animator.StringToHash("interact");
        protected static readonly int paramInteraction = Animator.StringToHash("interaction");
        protected static readonly int paramDiveDirection = Animator.StringToHash("dive_direction");
        protected static readonly int paramSkill = Animator.StringToHash("skill");
        protected static readonly int paramAttackIn = Animator.StringToHash("attack_in");
        protected static readonly int paramAttackOut = Animator.StringToHash("attack_out");
        protected static readonly int paramAttackMultiplier = Animator.StringToHash("attack_multiplier");

        [SerializeField, FormerlySerializedAs("renderer")] protected GameObject rendererRoot;
        [SerializeField] protected float defaultAttackFullTime;
        [SerializeField] protected float[] defaultAttackTime;
        [SerializeField] protected float[] skillFullTimes;
        [SerializeField] protected float diveInTime;
        [SerializeField] Vector2 diveOutTimeRange;
        [SerializeField] protected float diveOutTime;
        [SerializeField, ReadOnly] protected State currentState = State.None;
        [SerializeField, ReadOnly] protected State lastState = State.None;
        [SerializeField, ReadOnly] protected Interaction lastInteraction = Interaction.None;

        protected Animator animator;
        protected BodyParts bodyParts;
        Coroutine skillRoutine;
        Tween diveTween;

        protected virtual void Awake() {
            animator = GetComponent<Animator>();
            bodyParts = GetComponent<BodyParts>();
            SetUp();
        }

        public virtual void Idle() {
            ChangeState(State.Idle);
        }

        public virtual void Run() {
            ChangeState(State.Run);
        }

        public virtual void Death() {
            ChangeState(State.Death);
        }

        public virtual void DoNothing() {
            Interact(Interaction.None);
        }

        // for owned units on the board
        public virtual void DiveIn() {
            Interact(Interaction.Dive, (paramDiveDirection, 0));
            diveTween?.Kill();
            diveTween = transform.DOMove(MapVisual.Instance.PortalPosition, diveInTime).OnComplete(() => SetRendererVisibility(false));
        }

        // for battle units of the home team
        public virtual void DiveOut() {
            Interact(Interaction.Dive, (paramDiveDirection, 1));
            transform.position = MapVisual.Instance.PortalPosition;
            var delay = diveOutTime * diveOutTimeRange.x;
            var duration = diveOutTime * (diveOutTimeRange.y - diveOutTimeRange.x);
            diveTween?.Kill();
            diveTween = DOTween.Sequence().AppendInterval(delay).Append(transform.DOLocalMove(Vector3.zero, duration));
        }

        public virtual void Attack() {
            Interact(Interaction.Attack);
        }

        public virtual void InterruptAttack() {
            DoNothing();
        }

        /// <param name="variant">Hero-specific variant of the skill (UnitSkillStartedEvent.Variant).</param>
        public virtual void UseSkill(int variant) {
            Interact(Interaction.Skill);
        }

        public void InterruptSkill() {
            DoNothing();
            if (skillRoutine != null) {
                StopCoroutine(skillRoutine);
                skillRoutine = null;
            }
            OnSkillInterrupted();
        }

        /// <summary>Restores body parts a skill animation may have toggled.</summary>
        protected virtual void OnSkillInterrupted() { }

        /// <summary>Runs a timed skill animation; it stops if the skill is interrupted.</summary>
        protected void PlaySkillRoutine(IEnumerator routine) {
            if (skillRoutine != null) StopCoroutine(skillRoutine);
            skillRoutine = StartCoroutine(routine);
        }

        /// <summary>Animation speed of the next swing (>= 1, from UnitAttackStartedEvent.AnimationSpeed).</summary>
        public void SetAttackSpeed(float multiplier) {
            animator.SetFloat(paramAttackMultiplier, multiplier);
        }

        protected void ChangeState(State state) {
            if (state == currentState) return;

            lastState = currentState;
            currentState = state;
            animator.SetInteger(paramState, (int)currentState);
            animator.SetInteger(paramLastState, (int)lastState);
            animator.SetTrigger(paramChangeState);

            ModifyBodyParts();
        }

        protected void Interact(Interaction interaction, params (int, object)[] metaDatas) {
            foreach (var data in metaDatas) {
                switch (data.Item2) {
                    case int i:
                        animator.SetInteger(data.Item1, i);
                        break;
                    case float f:
                        animator.SetFloat(data.Item1, f);
                        break;
                    case bool b:
                        animator.SetBool(data.Item1, b);
                        break;
                }
            }

            lastInteraction = interaction;
            animator.SetInteger(paramInteraction, (int)lastInteraction);
            animator.SetTrigger(paramInteract);
        }

        protected virtual void SetUp() { }

        protected virtual void ModifyBodyParts() { }

        public virtual void SetRendererVisibility(bool value) {
            rendererRoot.SetActive(value);
        }

        public void ResetPosition() {
            diveTween?.Kill();
            transform.localPosition = Vector3.zero;
        }

        void OnDestroy() {
            diveTween?.Kill();
        }

        [Button]
        void quick() {
            rendererRoot = transform.GetChild(0).gameObject;
        }
    }
}
