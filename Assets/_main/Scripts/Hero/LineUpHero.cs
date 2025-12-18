using DG.Tweening;
using RExt.Extensions;
using Sirenix.OdinInspector;
using UnityEngine;

public class LineUpHero : Hero {
    [SerializeField] HeroPicker picker;
    
    Tween snapTween;

    protected override void Initialize() {
        base.Initialize();
        var mapLayerMask = 1 << MapVisual.Instance.Layer;
        var pickable = GameManager.Instance.Progress.Phase == MatchPhase.Preparation;
        picker.Initialize(this, mapLayerMask, pickable);
    }

    public void SetData(HeroTrait trait, HeroRank rank) {
        this.trait = trait;
        this.rank = rank;
        name = $"[LineUp]({rank}){trait.id}";
        rankIcon.sprite = AssetDB.Instance.GetRankIcon(rank);
        SetUpModel();
        abilities.ForEach(x=>x.ResetAll());
        mecanim.Idle();
        model.SetUniformScale(GameConfigs.HEROES_SCALE[this.rank]);
    }
    
    [Button]
    public void Evolve() {
        rank = rank.Next();
        name = $"[LineUp]({rank}){trait.id}";
        rankIcon.sprite = AssetDB.Instance.GetRankIcon(rank);
        GetAbility<HeroAttributes>().ResetAll();
        model.SetUniformScale(GameConfigs.HEROES_SCALE[rank]);
    }
    
    public void UpdatePosition(Node node) {
        snapTween?.Kill();
        snapTween = transform.DOMove(node.WorldPosition, 0.1f).SetEase(Ease.Linear);
    }

    public void SwitchPickable(bool value) {
        picker.SwitchPickable(value);
    }
}