using RExt.Patterns.Singleton;
using UnityEngine;

[CreateAssetMenu(menuName = "DB/PrefabDB", fileName = "PrefabDB")]
public class PrefabDB : ScriptableObjectSingleton<PrefabDB> {
    public DamageOverTimeArea DotArea => dotArea;
    
    [SerializeField] DamageOverTimeArea dotArea;
}