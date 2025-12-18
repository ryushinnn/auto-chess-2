using System;
using System.Collections;
using DG.Tweening;
using RExt.Utils;
using Sirenix.OdinInspector;
using UnityEngine;

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
    
    protected readonly int paramChangeState = Animator.StringToHash("change_state");
    protected readonly int paramState = Animator.StringToHash("state");
    protected readonly int paramLastState = Animator.StringToHash("last_state");
    protected readonly int paramIdleIn = Animator.StringToHash("idle_in");
    protected readonly int paramRunIn = Animator.StringToHash("run_in");
    protected readonly int paramInteract = Animator.StringToHash("interact");
    protected readonly int paramInteraction = Animator.StringToHash("interaction");
    protected readonly int paramDiveDirection = Animator.StringToHash("dive_direction");
    protected readonly int paramSkill= Animator.StringToHash("skill");
    protected readonly int paramAttackIn = Animator.StringToHash("attack_in");
    protected readonly int paramAttackOut = Animator.StringToHash("attack_out");
    protected readonly int paramAttackMultiplier = Animator.StringToHash("attack_multiplier");

    [SerializeField] protected GameObject renderer;
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
    protected float attackTimeMultiplier;
    protected Coroutine attackCoroutine;
    protected Coroutine useSkillCoroutine;

    protected virtual void Awake() {
        animator = GetComponent<Animator>();
        bodyParts = GetComponent<BodyParts>();
        attackTimeMultiplier = 1;
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

    // for line up hero only
    public virtual void DiveIn() {
        Interact(Interaction.Dive, (paramDiveDirection, 0));
        var portal = MapVisual.Instance.PortalPosition;
        transform.DOMove(portal, diveInTime).OnComplete(() => {
            SetRendererVisibility(false);
        });
    }

    // for battle hero only
    public virtual void DiveOut() {
        Interact(Interaction.Dive, (paramDiveDirection, 1));
        transform.position = MapVisual.Instance.PortalPosition;
        var delay = diveOutTime * diveOutTimeRange.x;
        var duration = diveOutTime * (diveOutTimeRange.y - diveOutTimeRange.x);
        DOVirtual.DelayedCall(delay, () => {
            transform.DOLocalMove(Vector3.zero, duration);
        });
    }

    public virtual void Attack() {
        Interact(Interaction.Attack);
    }

    public virtual void InterruptAttack() {
        DoNothing();
        if (attackCoroutine != null) {
            StopCoroutine(attackCoroutine);
        }
    }

    public virtual void UseSkill() {
        Interact(Interaction.Skill);
    }

    public virtual void InterruptSkill() {
        DoNothing();
        if (useSkillCoroutine != null) {
            StopCoroutine(useSkillCoroutine);
        }
    }

    public void ModifyAttackTime(float atkSpd) {
        var time = 1 / atkSpd;
        attackTimeMultiplier = Mathf.Max(1, defaultAttackFullTime / time);
        animator.SetFloat(paramAttackMultiplier, attackTimeMultiplier);
    }

    public void ModifyAttackTime_New(float atkTimeMul) {
        attackTimeMultiplier = atkTimeMul;
        animator.SetFloat(paramAttackMultiplier, attackTimeMultiplier);
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
    
    protected virtual void SetUp() {
        
    }

    protected virtual void ModifyBodyParts() {
        
    }
    
    public virtual void SetRendererVisibility(bool value) {
        renderer.SetActive(value);
    }

    public void ResetPosition() {
        transform.localPosition = Vector3.zero;
    }

    [Button]
    void quick() {
        renderer = transform.GetChild(0).gameObject;
    }
}