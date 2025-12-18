public class AttackProcessor_Irelia : AttackProcessor {
    public AttackProcessor_Irelia(BattleHero hero) : base(hero) {
        animationLength = 1.3f;
        timers = new[] { 0.2f };
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