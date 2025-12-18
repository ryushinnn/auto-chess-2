using System;

public class AttackProcessor_Yasuo : AttackProcessor {
    int count = 0;
    
    const float DMG_MUL = 0.5f;
    const int COUNT_LIMIT = 2;

    public AttackProcessor_Yasuo(BattleHero hero) : base(hero) {
        animationLength = 1.333f;
        timers = new[] { 0.33f };
        Description = $"Mỗi đòn đánh thứ {COUNT_LIMIT + 1} sẽ gây thêm " +
                      $"({DMG_MUL * 100}% <sprite name=pdmg>) sát thương chuẩn.";
    }

    public override void Process(float timer) {
        base.Process(timer);

        if (trueTimer >= timers[0] && atkExecuted == 0) {
            if (((BattleHero)hero).Target != null) {
                var baseDmg = attributes.GetDamage(DamageType.Physical);
                float outputDmg;
                if (count == COUNT_LIMIT) {
                    outputDmg = ((BattleHero)hero).Target.GetAbility<HeroAttributes>().TakeDamage(new[] {
                        baseDmg,
                        Damage.Create(baseDmg.value * DMG_MUL, DamageType.True, 0, baseDmg.crit),
                    });
                    count = 0;
                }
                else {
                    outputDmg = ((BattleHero)hero).Target.GetAbility<HeroAttributes>().TakeDamage(baseDmg);
                    count++;
                }

                var heal = outputDmg * attributes.LifeSteal;
                if (heal > 0) {
                    hero.GetAbility<HeroAttributes>().Heal(heal);
                }

                hero.GetAbility<HeroAttributes>().RegenEnergy(hero.Trait.energyRegenPerAttack);
            }

            atkExecuted++;
        }
    }
}