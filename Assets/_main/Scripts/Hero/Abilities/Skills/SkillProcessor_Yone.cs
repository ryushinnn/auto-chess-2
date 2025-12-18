public class SkillProcessor_Yone : SkillProcessor {
    readonly float divineBaseDmg;
    readonly float divineDmgMul;
    readonly float airborneDuration;
    readonly float energyRegen;
    readonly float devilBaseDmg0;
    readonly float devilDmgMul0;
    readonly float devilBaseDmg1;
    readonly float devilDmgMul1;
    
    const float DIVINE_ANIM_LENGTH = 4;
    const float DEVIL_ANIM_LENGTH = 4.25f;
    readonly float[] DIVINE_TIMERS = { 1.66f };
    readonly float[] DEVIL_TIMERS = { 0.12f, 0.72f, 1.08f, 2.24f };

    readonly AttackProcessor_Yone atkProcessor;
    YoneSword sword;
    
    public SkillProcessor_Yone(BattleHero hero) : base(hero) {
        var skillParams = hero.Trait.skillParams;
        divineBaseDmg = skillParams[5].value;
        divineDmgMul = skillParams[6].value;
        airborneDuration = skillParams[7].value;
        energyRegen = skillParams[8].value;
        devilBaseDmg0 = skillParams[9].value;
        devilDmgMul0 = skillParams[10].value;
        devilBaseDmg1 = skillParams[11].value;
        devilDmgMul1 = skillParams[12].value;

        atkProcessor = this.hero.GetAbility<HeroAttack>().Processor as AttackProcessor_Yone;
    }

    public override void Begin(out float animLength) {
        sword = atkProcessor.CurrentSword;
        animationLength = sword == YoneSword.Divine ? DIVINE_ANIM_LENGTH : DEVIL_ANIM_LENGTH;
        timers = sword == YoneSword.Divine ? DIVINE_TIMERS : DEVIL_TIMERS;
        base.Begin(out animLength);
    }

    public override void Process(float timer) {
        if (sword == YoneSword.Divine) {
            if (timer >= timers[0] && skillExecuted == 0) {
                Judge();
                skillExecuted++;
            }
        }
        else {
            if (timer >= timers[0] && skillExecuted == 0) {
                LightSmite();
                skillExecuted++;
            }
            else if (timer >= timers[1] && skillExecuted == 1) {
                LightSmite();
                skillExecuted++;
            }
            else if (timer >= timers[2] && skillExecuted == 2) {
                LightSmite();
                skillExecuted++;
            }
            else if (timer >= timers[3] && skillExecuted == 3) {
                HeavySmite();
                skillExecuted++;
            }
        }
    }

    void Judge() {
        if (hero.Target == null) return;
        
        hero.Target.GetAbility<HeroAttributes>().TakeDamage(attributes.GetDamage(DamageType.Physical,false,
            scaledValues:new[]{(divineDmgMul, DamageType.Physical)},
            fixedValues:new[]{ divineBaseDmg }));
        
        hero.Target.GetAbility<HeroStatusEffects>().Airborne(airborneDuration);
        
        if (!hero.Target.GetAbility<HeroAttributes>().IsAlive){
            attributes.RegenEnergy(energyRegen);
        }
    }

    void LightSmite() {
        if (hero.Target == null) return;
        
        hero.Target.GetAbility<HeroAttributes>().TakeDamage(attributes.GetDamage(DamageType.Magical, attributes.Crit(),
            scaledValues: new[] { (devilDmgMul0, DamageType.Physical) },
            fixedValues: new[] { devilBaseDmg0 }));
    }

    void HeavySmite() {
        if (hero.Target == null) return;
        
        hero.Target.GetAbility<HeroAttributes>().TakeDamage(attributes.GetDamage(DamageType.Magical,attributes.Crit(),
            scaledValues: new[] { (devilDmgMul1, DamageType.Physical) },
            fixedValues: new[] { devilBaseDmg1 }));
    }
}