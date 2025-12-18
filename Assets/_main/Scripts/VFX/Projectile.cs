using System;
using UnityEngine;

public class Projectile : Vfx {
    BattleHero dealer;
    BattleHero target;
    float velocity;
    Action onStrike;
    
    public void SetData(BattleHero dealer, BattleHero target, Action onStrike, float velocity = 20) {
        this.dealer = dealer;
        this.target = target;
        this.onStrike = onStrike;
        this.velocity = velocity;
        transform.position = dealer.WorldPosition;
    }

    void Update() {
        var direction = (target.WorldPosition - transform.position);
        var disPerFrame = velocity * Time.deltaTime;
        if (direction.magnitude <= disPerFrame) {
            if (dealer.GetAbility<HeroAttributes>().IsAlive) {
                onStrike?.Invoke();
            }
            VfxPool.Instance.DestroyVfx(this);
        }
        else {
            transform.rotation = Quaternion.LookRotation(direction);
            transform.position += direction.normalized * disPerFrame;
        }
    }
}