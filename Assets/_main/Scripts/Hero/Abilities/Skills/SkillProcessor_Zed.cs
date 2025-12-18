public class SkillProcessor_Zed : SkillProcessor {
    readonly float baseDmg;
    readonly float dmgMul;
    readonly int range;
    readonly float stunMain;
    readonly float stunOthers;
    
    Hero aimedTarget;

    public SkillProcessor_Zed(BattleHero hero) : base(hero) {
        animationLength = 3;
        timers = new[] { 0.83f, 1.66f };
        
        var skillParams = hero.Trait.skillParams;
        baseDmg = skillParams[0].value;
        dmgMul = skillParams[1].value;
        range = (int)skillParams[2].value;
        stunMain = skillParams[3].value;
        stunOthers = skillParams[4].value;
    }

    public override void Process(float timer) {
        if (timer >= timers[0] && skillExecuted == 0) {
            ThrowShurikens();
            skillExecuted++;
        }
        else if (timer >= timers[1] && skillExecuted == 1) {
            TriggerExplosion();
            skillExecuted++;
        }
    }

    void ThrowShurikens() {
        if (hero.Target == null) return;

        aimedTarget = hero.Target;
        aimedTarget.GetAbility<HeroAttributes>().TakeDamage(attributes.GetDamage(DamageType.Physical, false,
            scaledValues: new[] { (DMG_MUL: dmgMul, DamageType.Physical) }));
    }

    void TriggerExplosion() {
        if (aimedTarget == null) return;
        
        aimedTarget.GetAbility<HeroAttributes>().TakeDamage(attributes.GetDamage(DamageType.Physical,true,
            scaledValues: new[] { (1f, DamageType.Physical) }));
        aimedTarget.GetAbility<HeroStatusEffects>().Stun(stunMain);
        
        // same error with yasuo skill, review later
        
        // var affectedNodes = Map.Instance.GetCircle(aimedTarget.MNode.X, aimedTarget.MNode.Y, RANGE, true);
        // foreach (var node in affectedNodes) {
        //     if (!node.HasNone()) {
        //         node.Process(x => {
        //             if (x is Hero h && h.Side != hero.Side) {
        //                 h.GetAbility<HeroAttributes>().TakeDamage(
        //                     attribute.PhysicalDamage * attribute.CriticalDamage,
        //                     DamageType.Physical,
        //                     attribute.PhysicalPenetration);
        //                 
        //                 h.GetAbility<HeroStatusEffects>().Stun(h == aimedTarget ? STUN_MAIN : STUN_OTHERS);
        //             }
        //         });
        //     }
        // }
        
        aimedTarget = null;
    }
}