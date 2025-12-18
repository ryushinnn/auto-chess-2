public class SkillProcessor_Tristana : SkillProcessor {
    readonly float atkSpdMul;
    readonly float critChanceMul;
    readonly float duration;
    readonly string effectKey;
    
    public SkillProcessor_Tristana(BattleHero hero) : base(hero) {
        animationLength = 2.2f;
        timers = new[] { 0.625f };
        
        var skillParams = hero.Trait.skillParams;
        atkSpdMul = skillParams[0].value;
        critChanceMul = skillParams[1].value;
        duration = skillParams[2].value;
        
        var specialKeys = hero.Trait.specialKeys;
        effectKey = specialKeys[0];

        drainEnergy = true;
        drainEnergyDelay = timers[0];
        drainEnergyDuration = duration;
    }

    public override void Process(float timer) {
        if (timer >= timers[0] && skillExecuted == 0) {
            Greeting();
            skillExecuted++;
        }
    }

    void Greeting() {
        if (!attributes.IsAlive) return;
        
        attributes.AddAttributeModifier(
            AttributeModifierSet.Create(
                hero,
                effectKey,
                duration,
                new[] {
                    (AttributeModifierKey.AttackSpeed, atkSpdMul, AttributeModifier.Type.Percentage),
                    (AttributeModifierKey.CriticalChance, critChanceMul, AttributeModifier.Type.Percentage)
                }
            ));
    }
}