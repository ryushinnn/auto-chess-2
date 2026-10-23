using System;
using System.Collections.Generic;
using AutoChess.Core.Combat.Heroes;

namespace AutoChess.Core.Combat {
    /// <summary>Maps a unit definition id to its attack and skill behaviours (was the two processor factories).</summary>
    public sealed class AbilityRegistry {
        readonly Dictionary<string, Func<UnitDefinition, AttackBehaviour>> attacks = new(StringComparer.Ordinal);
        readonly Dictionary<string, Func<UnitDefinition, AttackBehaviour, SkillBehaviour>> skills = new(StringComparer.Ordinal);

        public static readonly AbilityRegistry Default = CreateDefault();

        public void Register(string unitId, Func<UnitDefinition, AttackBehaviour> attack,
            Func<UnitDefinition, AttackBehaviour, SkillBehaviour> skill) {
            if (attack != null) attacks[unitId] = attack;
            if (skill != null) skills[unitId] = skill;
        }

        public bool Has(string unitId) => attacks.ContainsKey(unitId) || skills.ContainsKey(unitId);

        public AttackBehaviour CreateAttack(UnitDefinition def) =>
            attacks.TryGetValue(def.Id, out var f) ? f(def) : new NoAttack();

        public SkillBehaviour CreateSkill(UnitDefinition def, AttackBehaviour attack) =>
            skills.TryGetValue(def.Id, out var f) ? f(def, attack) : new NoSkill();

        static AbilityRegistry CreateDefault() {
            var r = new AbilityRegistry();
            r.Register(HeroIds.AatroxDark, d => new AatroxDarkAttack(d), (d, _) => new AatroxDarkSkill(d));
            r.Register(HeroIds.AatroxTrueForm, _ => new AatroxTrueFormAttack(), null);
            r.Register(HeroIds.AatroxLight, d => new AatroxLightAttack(d), (d, _) => new AatroxLightSkill(d));
            r.Register(HeroIds.Akali, _ => new BasicAttack(1.2, 0.18, DamageType.Magical), (d, _) => new AkaliSkill(d));
            r.Register(HeroIds.Ashe, _ => new AsheAttack(), (d, _) => new AsheSkill(d));
            r.Register(HeroIds.Caitlyn, _ => new BasicAttack(1.367, 0.2, DamageType.Physical), (d, _) => new CaitlynSkill(d));
            r.Register(HeroIds.Irelia, _ => new BasicAttack(1.3, 0.2, DamageType.Physical), (d, _) => new IreliaSkill(d));
            r.Register(HeroIds.Jinx, _ => new BasicAttack(1.067, 0.37, DamageType.Physical), (d, _) => new JinxSkill(d));
            r.Register(HeroIds.Katarina, _ => new BasicAttack(1.067, 0.14, DamageType.Physical), (d, _) => new KatarinaSkill(d));
            r.Register(HeroIds.Malphite, _ => new BasicAttack(1.1, 0.17, DamageType.Physical), (d, _) => new MalphiteSkill(d));
            r.Register(HeroIds.MissFortune, d => new MissFortuneAttack(d), (d, _) => new MissFortuneSkill(d));
            r.Register(HeroIds.Morgana, _ => new BasicAttack(2d, 0.14, DamageType.Magical), (d, _) => new MorganaSkill(d));
            r.Register(HeroIds.Teemo, d => new TeemoAttack(d), (d, _) => new TeemoSkill(d));
            r.Register(HeroIds.Tristana, _ => new BasicAttack(0.933, 0.17, DamageType.Physical), (d, _) => new TristanaSkill(d));
            r.Register(HeroIds.Yasuo, _ => new YasuoAttack(), (d, _) => new YasuoSkill(d));
            r.Register(HeroIds.Yone, d => new YoneAttack(d), (d, a) => new YoneSkill(d, a as YoneAttack));
            r.Register(HeroIds.Zed, _ => new BasicAttack(1.267, 0.25, DamageType.Physical), (d, _) => new ZedSkill(d));
            r.Register(HeroIds.ZedShadow, _ => new BasicAttack(1.267, 0.25, DamageType.Physical), null);
            r.Register(HeroIds.Ahri, _ => new BasicAttack(1.067, 0.35, DamageType.Magical), (d, _) => new AhriSkill(d));
            r.Register(HeroIds.Amumu, _ => new BasicAttack(1.1, 0.35, DamageType.Physical), (d, _) => new AmumuSkill(d));
            r.Register(HeroIds.Annie, _ => new BasicAttack(1.333, 0.45, DamageType.Magical), (d, _) => new AnnieSkill(d));
            r.Register(HeroIds.Blitzcrank, _ => new BasicAttack(1.167, 0.4, DamageType.Physical), (d, _) => new BlitzcrankSkill(d));
            r.Register(HeroIds.Gwen, _ => new BasicAttack(1.3, 0.4, DamageType.Magical), (d, _) => new GwenSkill(d));
            r.Register(HeroIds.Janna, _ => new BasicAttack(1.833, 0.6, DamageType.Magical), (d, _) => new JannaSkill(d));
            r.Register(HeroIds.Kaisa, d => new KaisaAttack(d), (d, _) => new KaisaSkill(d));
            r.Register(HeroIds.LeeSin, _ => new BasicAttack(0.867, 0.3, DamageType.Physical), (d, _) => new LeeSinSkill(d));
            r.Register(HeroIds.Lulu, _ => new BasicAttack(1.133, 0.4, DamageType.Magical), (d, _) => new LuluSkill(d));
            r.Register(HeroIds.Lux, _ => new BasicAttack(1.4, 0.45, DamageType.Magical), (d, _) => new LuxSkill(d));
            return r;
        }
    }

    /// <summary>Hero ids used in data (HeroTrait.id, Stage enemies).</summary>
    public static class HeroIds {
        public const string Dummy = "_Dummy";
        public const string AatroxDark = "Aatrox_Dark";
        public const string AatroxLight = "Aatrox_Light";
        public const string AatroxTrueForm = "Aatrox_TrueForm";
        public const string Akali = "Akali";
        public const string Ashe = "Ashe";
        public const string Caitlyn = "Caitlyn";
        public const string Irelia = "Irelia";
        public const string Jinx = "Jinx";
        public const string Katarina = "Katarina";
        public const string Malphite = "Malphite";
        public const string MissFortune = "MissFortune";
        public const string Morgana = "Morgana";
        public const string Teemo = "Teemo";
        public const string Tristana = "Tristana";
        public const string Yasuo = "Yasuo";
        public const string Yone = "Yone";
        public const string Zed = "Zed";
        public const string ZedShadow = "Zed_Shadow";
        public const string Ahri = "Ahri";
        public const string Amumu = "Amumu";
        public const string Annie = "Annie";
        public const string Blitzcrank = "Blitzcrank";
        public const string Gwen = "Gwen";
        public const string Janna = "Janna";
        public const string Kaisa = "Kaisa";
        public const string LeeSin = "LeeSin";
        public const string Lulu = "Lulu";
        public const string Lux = "Lux";
    }
}
