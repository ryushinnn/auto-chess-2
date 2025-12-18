using RExt.Extensions;
using Sirenix.OdinInspector;
using UnityEngine;

public class BattleHero : Hero {
    public TeamSide Side => side;
    public BattleHero Target => target;
    
    [SerializeField, ReadOnly] TeamSide side;
    [SerializeField, ReadOnly] BattleHero target;
    HeroBT bt;

    public void SetData(HeroTrait trait, HeroRank rank, TeamSide side) {
        this.trait = trait;
        this.side = side;
        this.rank = rank;
        name = $"({rank}){trait.id}";
        rankIcon.sprite = AssetDB.Instance.GetRankIcon(rank);
        SetUpModel();
        abilities.ForEach(x=>x.ResetAll());
        mecanim.Idle();
        model.SetUniformScale(GameConfigs.HEROES_SCALE[this.rank]);
    }
    
    public void FindTarget() {
        target = GameManager.Instance.BattleField.GetNearestOpponent(this);
    }

    public void SetBehaviour(HeroBT bt) {
        this.bt = bt;
    }

    protected override void Process() {
        base.Process();
        bt?.Evaluate();
    }

    [Button]
    void checkBt() {
        Debug.Log(bt != null);
    }
}