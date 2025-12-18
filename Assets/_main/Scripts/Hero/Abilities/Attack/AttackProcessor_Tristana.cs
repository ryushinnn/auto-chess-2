public class AttackProcessor_Tristana : AttackProcessor {
    public AttackProcessor_Tristana(BattleHero hero) : base(hero) {
        animationLength = 0.933f;
        timers = new[] { 0.17f };
    }

    public override void Process(float timer) {
        base.Process(timer);

        if (trueTimer >= timers[0] && atkExecuted == 0) {
            if (((BattleHero)hero).Target != null) {
                var outputDmg = ((BattleHero)hero).Target.GetAbility<HeroAttributes>().TakeDamage(attributes.GetDamage(DamageType.Physical));
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