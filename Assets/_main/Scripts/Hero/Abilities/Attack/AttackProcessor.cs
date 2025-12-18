using System;
using System.Linq;
using RExt.Extensions;
using UnityEngine;

[Serializable]
public abstract class AttackProcessor {
    protected float animationLength;
    protected float[] timers;
    public string Description { get; protected set; }

    protected float atkTimeMul;
    protected int atkExecuted;
    protected float trueTimer;
    
    protected readonly BattleHero hero;
    protected readonly HeroAttributes attributes;
    
    protected AttackProcessor(BattleHero hero) {
        this.hero = hero;
        attributes = this.hero.GetAbility<HeroAttributes>();
    }

    public virtual void Begin(out float actualAnimLength) {
        var expectedAnimLength = 1 / attributes.AttackSpeed;
        atkTimeMul = Mathf.Max(1, animationLength / expectedAnimLength);
        actualAnimLength = animationLength / atkTimeMul;
        hero.Mecanim.ModifyAttackTime_New(atkTimeMul);
        atkExecuted = 0;
    }

    public virtual void Process(float timer) {
        trueTimer = timer * atkTimeMul;
    }

    public virtual void End(bool complete) { }
}