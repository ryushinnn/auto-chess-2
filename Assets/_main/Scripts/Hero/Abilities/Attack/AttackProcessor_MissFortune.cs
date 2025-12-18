using System;

public class AttackProcessor_MissFortune : AttackProcessor {
    readonly float pDmgMul0;
    readonly float mDmgMul0;
    readonly float pDmgMul1;
    readonly float mDmgMul1;
    

    public AttackProcessor_MissFortune(BattleHero hero) : base(hero) {
        animationLength = 1.167f;
        timers = new[] { 0.35f };
        
        var skillParams = hero.Trait.skillParams;
        pDmgMul0 = skillParams[0].value;
        mDmgMul0 = skillParams[1].value;
        pDmgMul1 = skillParams[2].value;
        mDmgMul1 = skillParams[3].value;
    }

    public override void Process(float timer) {
        base.Process(timer);

        if (trueTimer >= timers[0] && atkExecuted == 0) {
            if (hero.Target != null) {
                var crit = attributes.Crit();
                var phyDmg = attributes.GetDamage(DamageType.Physical, crit, scaledValues: new[] {
                    (pDmgMul0, DamageType.Physical),
                    (mDmgMul0, DamageType.Magical)
                });
                var magDmg = attributes.GetDamage(DamageType.Magical, crit, scaledValues: new[] {
                    (pDmgMul1, DamageType.Physical),
                    (mDmgMul1, DamageType.Magical)
                });
                var outputDmg = hero.Target.GetAbility<HeroAttributes>().TakeDamage(new[] { phyDmg, magDmg });
                var heal = outputDmg * attributes.LifeSteal;
                if (heal > 0) {
                    attributes.Heal(heal);
                }
                attributes.RegenEnergy(hero.Trait.energyRegenPerAttack);
            }

            atkExecuted++;
        }
    }
}