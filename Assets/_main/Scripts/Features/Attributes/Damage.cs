public class Damage {
    public float value;
    public DamageType type;
    public float penetration;
    public bool crit;

    public static Damage Create(float value, DamageType type, float penetration, bool crit = false) {
        return new Damage {
            value = value,
            type = type,
            penetration = penetration,
            crit = crit
        };
    }

    public static Damage Create(Damage damage) {
        return new Damage {
            value = damage.value,
            type = damage.type,
            penetration = damage.penetration,
            crit = damage.crit
        };
    }
}