public class SkillProcessor_Irelia : SkillProcessor {
    readonly float baseDmg;
    readonly float dmgMul;
    readonly float airborneDuration;
    
    public SkillProcessor_Irelia(BattleHero hero) : base(hero) {
        animationLength = 4.8f;
        timers = new[] { 4.23f };
        
        var skillParams = hero.Trait.skillParams;
        baseDmg = skillParams[0].value;
        dmgMul = skillParams[1].value;
        airborneDuration = skillParams[2].value;
    }

    public override void Process(float timer) {
        if (timer >= timers[0] && skillExecuted == 0) {
            SummonDragon();
            skillExecuted++;
        }
    }

    void SummonDragon() {
        if (hero.Target == null) return;

        hero.Target.GetAbility<HeroAttributes>().TakeDamage(attributes.GetDamage(DamageType.Magical, false,
            scaledValues: new[]{(dmgMul, DamageType.Physical)},
            fixedValues: new[]{ baseDmg }));
        hero.Target.GetAbility<HeroStatusEffects>().Airborne(airborneDuration);
    }
}