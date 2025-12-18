public static class AttackProcessorFactory {
    public static AttackProcessor Create(BattleHero hero) {
        return hero.Trait.id switch {
            HeroId.Aatrox_Dark => new AttackProcessor_Aatrox_Dark(hero),
            HeroId.Aatrox_Light => new AttackProcessor_Aatrox_Light(hero),
            HeroId.Akali => new AttackProcessor_Akali(hero),
            HeroId.Ashe => new AttackProcessor_Ashe(hero),
            HeroId.Caitlyn => new AttackProcessor_Caitlyn(hero),
            HeroId.Irelia => new AttackProcessor_Irelia(hero),
            HeroId.Jinx => new AttackProcessor_Jinx(hero),
            HeroId.Katarina => new AttackProcessor_Katarina(hero),
            HeroId.Malphite => new AttackProcessor_Malphite(hero),
            HeroId.MissFortune => new AttackProcessor_MissFortune(hero),
            HeroId.Morgana => new AttackProcessor_Morgana(hero),
            HeroId.Teemo => new AttackProcessor_Teemo(hero),
            HeroId.Tristana => new AttackProcessor_Tristana(hero),
            HeroId.Yasuo => new AttackProcessor_Yasuo(hero),
            HeroId.Yone => new AttackProcessor_Yone(hero),
            HeroId.Zed => new AttackProcessor_Zed(hero),
            _ => new AttackProcessor__Empty(hero)
        };
    }
}