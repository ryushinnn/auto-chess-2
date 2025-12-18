public class SkillProcessor_MissFortune : SkillProcessor {
    readonly float lPDmgMul0;
    readonly float lMDmgMul0;
    readonly float lPDmgMul1;
    readonly float lMDmgMul1;
    readonly float rPDmgMul0;
    readonly float rMDmgMul0;
    readonly float rPDmgMul1;
    readonly float rMDmgMul1;
    readonly float vampMul;
    readonly float reduceDefMul;
    readonly float reduceDefDuration;
    readonly string effectKey;

    public SkillProcessor_MissFortune(BattleHero hero) : base(hero) {
        animationLength = 2.8f;
        timers = new[] { 1f, 1.3f };
        
        var skillParams = hero.Trait.skillParams;
        lPDmgMul0 = skillParams[0].value;
        lMDmgMul0 = skillParams[1].value;
        lPDmgMul1 = skillParams[2].value;
        lMDmgMul1 = skillParams[3].value;
        rPDmgMul0 = skillParams[4].value;
        rMDmgMul0 = skillParams[5].value;
        rPDmgMul1 = skillParams[6].value;
        rMDmgMul1 = skillParams[7].value;
        vampMul = skillParams[8].value;
        reduceDefMul = skillParams[9].value;
        reduceDefDuration = skillParams[10].value;
        
        var specialKeys = hero.Trait.specialKeys;
        effectKey = specialKeys[0];
    }

    public override void Process(float timer) {
        if (timer >= timers[0] && skillExecuted == 0) {
            ShotLeft();
            skillExecuted++;
        }
        else if (timer >= timers[1] && skillExecuted == 1) {
            ShotRight();
            skillExecuted++;
        }
    }

    void ShotLeft() {
        if (hero.Target == null) return;

        var crit = attributes.Crit();
        var phyDmg = attributes.GetDamage(DamageType.Physical, crit, scaledValues:
            new[] {
                (lPDmgMul0, DamageType.Physical),
                (lMDmgMul0, DamageType.Magical)
            });
        var magDmg = attributes.GetDamage(DamageType.Magical, crit, scaledValues:
            new[] {
                (lPDmgMul1, DamageType.Physical),
                (lMDmgMul1, DamageType.Magical)
            });
        
        var outputDmg = hero.Target.GetAbility<HeroAttributes>().TakeDamage(new[] {phyDmg, magDmg});
        attributes.Heal(outputDmg * vampMul);
    }

    void ShotRight() {
        if (hero.Target == null) return;

        var crit = attributes.Crit();
        var phyDmg = attributes.GetDamage(DamageType.Physical, crit, scaledValues:
            new[] {
                (rPDmgMul0, DamageType.Physical),
                (rMDmgMul0, DamageType.Magical)
            });
        var magDmg = attributes.GetDamage(DamageType.Magical, crit, scaledValues:
            new[] {
                (rPDmgMul1, DamageType.Physical),
                (rMDmgMul1, DamageType.Magical)
            });
        hero.Target.GetAbility<HeroAttributes>().TakeDamage(new[] {phyDmg, magDmg});
        
        hero.Target.GetAbility<HeroAttributes>().AddAttributeModifier(
            AttributeModifierSet.Create(
                hero,
                effectKey,
                reduceDefDuration,
                new[] {
                    (AttributeModifierKey.Armor, reduceDefMul, AttributeModifier.Type.Percentage),
                    (AttributeModifierKey.Resistance, reduceDefMul, AttributeModifier.Type.Percentage),
                }
            ));
    }
}