namespace AutoChess.Core.Combat {
    public struct Damage {
        public float Value;
        public DamageType Type;
        public float Penetration;
        public bool Crit;

        public Damage(float value, DamageType type, float penetration, bool crit = false) {
            Value = value;
            Type = type;
            Penetration = penetration;
            Crit = crit;
        }

        public override string ToString() => $"{Value:0.#} {Type}{(Crit ? " crit" : "")}";
    }
}
