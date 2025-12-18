using DG.Tweening;
using Sirenix.OdinInspector;
using UnityEngine;

public class HeroStatusEffects : HeroAbility {
    public bool IsAirborne => isAirborne;
    public bool IsUnstoppable => isUnstoppable;
    public bool IsStun => isStun;
    public bool IsSilent => isSilent;
    public bool IsAntiHeal => isAntiHeal;

    HeroAttributes attributes;

    [SerializeField, ReadOnly] bool isAirborne;
    [SerializeField, ReadOnly, HorizontalGroup("UNSTOPPABLE"), LabelWidth(100)] bool isUnstoppable;
    [SerializeField, ReadOnly, HorizontalGroup("STUN"), LabelWidth(100)]        bool isStun;
    [SerializeField, ReadOnly, HorizontalGroup("SILENT"), LabelWidth(100)]      bool isSilent;
    [SerializeField, ReadOnly, HorizontalGroup("ANTI_HEAL"), LabelWidth(100)]   bool isAntiHeal;
    
    Sequence airborneSequence;
    [SerializeField, ReadOnly, HorizontalGroup("STUN"), LabelText(""), ShowIf("isStun")]          float stunDuration;
    [SerializeField, ReadOnly, HorizontalGroup("SILENT"), LabelText(""), ShowIf("isSilent")]      float silenceDuration;
    [SerializeField, ReadOnly, HorizontalGroup("ANTI_HEAL"), LabelText(""), ShowIf("isAntiHeal")] float antiHealDuration;
    
    const float AIRBORNE_MAX_HEIGHT = 2;

    public override void ResetAll() {
        isAirborne = false;
        isUnstoppable = false;
        isStun = false;
        isSilent = false;
        isAntiHeal = false;
        airborneSequence?.Kill();
        stunDuration = 0;
        silenceDuration = 0;
        antiHealDuration = 0;
    }

    public override void Process() {
        if (isStun) {
            stunDuration -= Time.deltaTime;
            if (stunDuration <= 0) {
                isStun = false;
            }
        }
        
        if (isSilent) {
            silenceDuration -= Time.deltaTime;
            if (silenceDuration <= 0) {
                isSilent = false;
            }
        }

        if (isAntiHeal) {
            antiHealDuration -= Time.deltaTime;
            if (antiHealDuration <= 0) {
                isAntiHeal = false;
            }
        }
    }

    protected override void FindReferences() {
        attributes = hero.GetAbility<HeroAttributes>();
    }

    public void Airborne(float duration) {
        if (isUnstoppable) return;
        
        var currentHeight = hero.Model.localPosition.y;
        var goUpDistance = AIRBORNE_MAX_HEIGHT - currentHeight;
        var goDownDistance = AIRBORNE_MAX_HEIGHT;
        var goUpTime = duration / (goUpDistance + goDownDistance) * goUpDistance;
        var goDownTime = duration - goUpTime;
        isAirborne = true;
        airborneSequence?.Kill();
        airborneSequence = DOTween.Sequence()
            .Append(hero.Model.DOLocalMoveY(AIRBORNE_MAX_HEIGHT, goUpTime).SetEase(Ease.OutExpo))
            .Append(hero.Model.DOLocalMoveY(0, goDownTime).SetEase(Ease.InExpo))
            .AppendCallback(()=>isAirborne = false);
        
        hero.GetAbility<HeroAttack>().Interrupt();
        hero.GetAbility<HeroSkill>().Interrupt();
        hero.GetAbility<HeroMovement>().StopMove(true);
    }
    
    public void Unstoppable(bool value) {
        isUnstoppable = value;
    }

    public void Stun(float duration) {
        if (isUnstoppable) return;

        duration *= (1-attributes.Tenacity);
        isStun = true;
        stunDuration = Mathf.Max(stunDuration, duration);
        hero.GetAbility<HeroAttack>().Interrupt();
        hero.GetAbility<HeroSkill>().Interrupt();
        hero.GetAbility<HeroMovement>().StopMove(true);
    }
    
    public void Silent(float duration) {
        if (isUnstoppable) return;
        
        duration *= (1-attributes.Tenacity);
        isSilent = true;
        silenceDuration = Mathf.Max(silenceDuration, duration);
        hero.GetAbility<HeroSkill>().Interrupt();
    }
    
    public void AntiHeal(float duration) {
        isAntiHeal = true;
        antiHealDuration = Mathf.Max(antiHealDuration, duration);
    }
    
    [Button]
    void Dev_Stun() {
        Stun(1);
    }
    
    [Button]
    void Dev_Silent() {
        Silent(1);
    }
}