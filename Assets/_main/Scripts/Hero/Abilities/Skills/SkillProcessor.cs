using System;
using DG.Tweening;

public abstract class SkillProcessor {
    protected float animationLength;
    protected float[] timers;
    protected bool unstoppable;
    public string Name { get; protected set; }
    public string Description { get; protected set; }
    
    protected readonly BattleHero hero;
    protected readonly HeroAttributes attributes;

    protected bool drainEnergy;
    protected float drainEnergyDelay;
    protected float drainEnergyDuration;
    
    protected int skillExecuted;

    protected SkillProcessor(BattleHero hero) {
        this.hero = hero;
        unstoppable = this.hero.Trait.unstoppable;
        attributes = this.hero.GetAbility<HeroAttributes>();
    }

    public virtual void Begin(out float animLength) {
        if (unstoppable) {
            hero.GetAbility<HeroStatusEffects>().Unstoppable(true);
        }

        if (drainEnergy) {
            attributes.DrainEnergy(drainEnergyDelay, drainEnergyDuration);
        }
        else {
            attributes.UseAllEnergy();
        }
        animLength = animationLength;
        skillExecuted = 0;
    }

    public virtual void Process(float timer) {
        
    }

    public virtual void End(bool complete) {
        if (unstoppable) {
            hero.GetAbility<HeroStatusEffects>().Unstoppable(false);
        }
    }
}