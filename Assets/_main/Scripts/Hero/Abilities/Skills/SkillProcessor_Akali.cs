public class SkillProcessor_Akali : SkillProcessor {
    readonly float energyThreshold;
    readonly float silentDuration;
    readonly float stunDuration;
    
    public SkillProcessor_Akali(BattleHero hero) : base(hero) {
        animationLength = 1;
        timers = new[] { 0.2f };
        
        var skillParams = hero.Trait.skillParams;
        energyThreshold = skillParams[0].value;
        silentDuration = skillParams[1].value;
        stunDuration = skillParams[2].value;
    }

    public override void Process(float timer) {
        if (timer >= timers[0] && skillExecuted == 0) {
            ThrowBomb();
            skillExecuted++;
        }
    }

    void ThrowBomb() {
        if (hero.Target == null) return;

        if (hero.Target.GetAbility<HeroAttributes>().Energy >= energyThreshold) {
            hero.Target.GetAbility<HeroStatusEffects>().Silent(silentDuration);
        }
        else {
            hero.Target.GetAbility<HeroStatusEffects>().Stun(stunDuration);
        }
    }
}