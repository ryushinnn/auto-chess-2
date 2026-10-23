namespace AutoChess.Core.Combat {
    public struct Damage {
        public double Value;
        public DamageType Type;
        public double Penetration;
        public bool Crit;

        public Damage(double value, DamageType type, double penetration, bool crit = false) {
            Value = value;
            Type = type;
            Penetration = penetration;
            Crit = crit;
        }

        public override string ToString() => $"{Value:0.#} {Type}{(Crit ? " crit" : "")}";
    }
}
