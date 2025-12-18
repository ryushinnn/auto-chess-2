using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

public class DamageOverTimeArea : MonoBehaviour {
    [SerializeField] LayerMask targetLayerMask;
    [SerializeField] Transform graphic;

    BattleHero dealer;
    Damage damage;
    int strikes;
    float interval;
    bool canCrit;
    float radius;
    Vfx vfx;
    Action<List<BattleHero>> onStrike;

    float critChance;
    float critDamage;
    float timer;
    List<Hero> targets = new();
    Collider[] hits = new Collider[10];
    
    public void SetData(BattleHero dealer, Damage damage, int strikes, float interval, bool canCrit, float radius,
        Vfx vfx = null, Action<List<BattleHero>> onStrike = null) {
        
        this.dealer = dealer;
        this.damage = damage;
        this.strikes = strikes;
        timer = this.interval = interval;
        this.canCrit = canCrit;
        this.radius = radius;
        this.vfx = vfx;
        this.onStrike = onStrike;
        
        if (this.canCrit) {
            var att = dealer.GetAbility<HeroAttributes>();
            critChance = att.CriticalChance;
            critDamage = att.CriticalDamage;
        }

        if (this.vfx) {
            this.vfx.transform.position = transform.position;
        }

        graphic.localScale = new Vector3(radius * 2, radius * 2, 1);
        targets.Clear();
    }

    void Update() {
        if (timer > 0) {
            timer -= Time.deltaTime;
        }
        else {
            Strike();
            if (--strikes == 0) {
                if (vfx) {
                    VfxPool.Instance.DestroyVfx(vfx);
                }
                Destroy(gameObject);
            }
            else {
                timer = interval;
            }
        }
    }

    void Strike() {
        var count = Physics.OverlapSphereNonAlloc(transform.position, radius, hits, targetLayerMask);
        var targetsAffectedByThisStrike = new List<BattleHero>();
        if (count > 0) {
            for (int i = 0; i < count; i++) {
                if (hits[i].TryGetComponent(out BattleHero h) && h.Side != dealer.Side) {
                    var isNewTarget = !targets.Contains(h);
                    var d = Damage.Create(damage);
                    if (canCrit && Random.value < critChance) {
                        d.value *= critDamage;
                        d.crit = true;
                    }
                    h.GetAbility<HeroAttributes>().TakeDamage(d, isNewTarget);
                    if (isNewTarget) {
                        targets.Add(h);
                    }
                    targetsAffectedByThisStrike.Add(h);
                }
            }
        }
        onStrike?.Invoke(targetsAffectedByThisStrike);
    }
}