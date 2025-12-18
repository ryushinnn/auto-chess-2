using System;

public abstract class EffectOverTime {
    public string key;
    public string id;
    public Hero owner;
    public int times;
    public float interval;
    public Mark mark;
    public float timer;
}

[Serializable]
public class DamageOverTime : EffectOverTime {
    public Damage damage;
    public int stacks;
    
    public static DamageOverTime Create(string key, Hero owner, Damage damage, int times, float interval, int stacks = 1, bool applyDmgInstantly = true, bool createMark = true) {
        return new DamageOverTime {
            id = Guid.NewGuid().ToString(),
            key = key,
            owner = owner,
            damage = damage,
            times = times,
            interval = interval,
            stacks = stacks,
            mark = createMark 
                ? Mark.Create(
                    owner, 
                    key, 
                    stacks, 
                    interval * (applyDmgInstantly ? times-1 : times)
                ) 
                : null, 
            timer = applyDmgInstantly ? 0 : interval,
        };
    }

    public bool SameAs(DamageOverTime other) {
        return key == other.key && owner == other.owner;
    }
}

[Serializable]
public class HealOverTime : EffectOverTime {
    public float amount;
    
    public static HealOverTime Create(string key, Hero owner, float amount, int times, float interval, bool createMark = false) {
        return new HealOverTime {
            id = Guid.NewGuid().ToString(),
            key = key,
            owner = owner,
            amount = amount,
            times = times,
            interval = interval,
            mark = createMark 
                ? Mark.Create(
                    owner,
                    key,
                    1, 
                    interval * (times-1)
                ) 
                : null,
            timer = 0,
        };
    }
    
    public bool SameAs(HealOverTime other) {
        return key == other.key && owner == other.owner;
    }
}