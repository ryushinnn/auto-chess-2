using System.Collections.Generic;
using System.Linq;

public class DestinyProcessor_Mortal : DestinyProcessor {
    readonly float[] attributeBonuses;
    readonly float mDmg;
    readonly float pDmg;
    readonly float energyRegenEff;
    readonly float critChance;
    readonly float critDmg;
    readonly float penetration;
    
    public DestinyProcessor_Mortal(DestinyConfig cfg) : base(cfg) {
        var destinyParams = cfg.destinyParams;
        
        attributeBonuses = new[] {
            destinyParams[0].value, 
            destinyParams[1].value, 
            destinyParams[2].value, 
            destinyParams[3].value
        };
        mDmg = destinyParams[4].value;
        pDmg = destinyParams[5].value;
        energyRegenEff = destinyParams[6].value;
        critChance = destinyParams[7].value;
        critDmg = destinyParams[8].value;
        penetration = destinyParams[9].value;
    }

    public override void Activate(List<BattleHero> heroes, int checkpointIndex) {
        foreach (var hero in heroes) {
            if (hero.Side == TeamSide.Enemy) continue;

            AttributeModifierSet modifierSet = null;
            var mul = 1 + attributeBonuses[checkpointIndex];
            switch (hero.Trait.id) {
                case HeroId.Akali:
                    modifierSet = AttributeModifierSet.Create(
                        hero,
                        "mortal",
                        new[] {
                            (AttributeModifierKey.MagicalDamage, mDmg * mul, AttributeModifier.Type.FixedValue),
                        },
                        createMark:false);
                    break;
                
                case HeroId.Jinx:
                    modifierSet = AttributeModifierSet.Create(
                        hero,
                        "mortal",
                        new[] {
                            (AttributeModifierKey.PhysicalDamage, pDmg * mul, AttributeModifier.Type.FixedValue),
                        },
                        createMark:false);
                    break;
                
                case HeroId.Caitlyn:
                    modifierSet = AttributeModifierSet.Create(
                        hero,
                        "mortal",
                        new[] {
                            (AttributeModifierKey.EnergyRegenEfficient, energyRegenEff * mul, AttributeModifier.Type.FixedValue),
                        },
                        createMark:false);
                    break;
                
                case HeroId.Katarina:
                    modifierSet = AttributeModifierSet.Create(
                        hero,
                        "mortal",
                        new[] {
                            (AttributeModifierKey.CriticalChance, critChance * mul, AttributeModifier.Type.FixedValue),
                        },
                        createMark:false);
                    break;
                
                case HeroId.Tristana:
                    modifierSet = AttributeModifierSet.Create(
                        hero,
                        "mortal",
                        new[] {
                            (AttributeModifierKey.CriticalDamage, critDmg * mul, AttributeModifier.Type.FixedValue),
                        },
                        createMark:false);
                    break;
                
                case HeroId.MissFortune:
                    modifierSet = AttributeModifierSet.Create(
                        hero,
                        "mortal",
                        new[] {
                            (AttributeModifierKey.MagicalPenetration, penetration * mul, AttributeModifier.Type.FixedValue),
                            (AttributeModifierKey.PhysicalPenetration, penetration * mul, AttributeModifier.Type.FixedValue)
                        },
                        createMark:false);
                    break;
                    
            }

            if (modifierSet != null) {
                hero.GetAbility<HeroAttributes>().AddAttributeModifier(modifierSet);
            }
        }
    }
}