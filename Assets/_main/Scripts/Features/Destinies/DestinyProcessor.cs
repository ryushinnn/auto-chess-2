using System.Collections.Generic;

public abstract class DestinyProcessor {
    protected DestinyProcessor(DestinyConfig cfg) { }
    public abstract void Activate(List<BattleHero> heroes, int checkpointIndex);
}