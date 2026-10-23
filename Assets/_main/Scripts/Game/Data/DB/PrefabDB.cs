using RExt.Patterns.Singleton;
using UnityEngine;

namespace AutoChess.Game {
    [CreateAssetMenu(menuName = "DB/PrefabDB", fileName = "PrefabDB")]
    public class PrefabDB : ScriptableObjectSingleton<PrefabDB> {
        public AreaView DotArea => dotArea;

        [SerializeField] AreaView dotArea;
    }
}
