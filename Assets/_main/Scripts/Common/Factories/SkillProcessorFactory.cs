public static class SkillProcessorFactory {
    public static SkillProcessor Create(BattleHero hero) {
        return hero.Trait.id switch {
            HeroId.Aatrox_Dark => new SkillProcessor_Aatrox_Dark(hero),
            HeroId.Aatrox_Light => new SkillProcessor_Aatrox_Light(hero),
            HeroId.Akali => new SkillProcessor_Akali(hero),
            HeroId.Ashe => new SkillProcessor_Ashe(hero),
            HeroId.Caitlyn => new SkillProcessor_Caitlyn(hero),
            HeroId.Irelia => new SkillProcessor_Irelia(hero),
            HeroId.Jinx => new SkillProcessor_Jinx(hero),
            HeroId.Katarina => new SkillProcessor_Katarina(hero),
            HeroId.Malphite => new SkillProcessor_Malphite(hero),
            HeroId.MissFortune => new SkillProcessor_MissFortune(hero),
            HeroId.Morgana => new SkillProcessor_Morgana(hero),
            HeroId.Teemo => new SkillProcessor_Teemo(hero),
            HeroId.Tristana => new SkillProcessor_Tristana(hero),
            HeroId.Yasuo => new SkillProcessor_Yasuo(hero),
            HeroId.Yone => new SkillProcessor_Yone(hero),
            HeroId.Zed => new SkillProcessor_Zed(hero),
            _ => new SkillProcessor__Empty(hero),
        };
    }
}