using System.Collections.Generic;
using System.Linq;
using RExt.Extensions;
using UnityEngine;

public class Destinies : MonoBehaviour {
    Dictionary<Role,int> roleNumbers = new();
    Dictionary<Realm,int> realmNumbers = new();
    HashSet<HeroTrait> uniqueTraits = new();

    public void Initialize() {
        UIManager_Arena.Instance.Destinies.SetData(roleNumbers, realmNumbers);
    }

    public void SetData(HeroTrait[] traits) {
        roleNumbers.Clear();
        realmNumbers.Clear();
        uniqueTraits.Clear();
        
        foreach (var trait in traits) {
            if (uniqueTraits.Add(trait)) {
                var roles = trait.role.GetAllFlags().Where(x => x != 0).ToArray();
                foreach (var role in roles) {
                    if (!roleNumbers.TryAdd(role, 1)) {
                        roleNumbers[role]++;
                    }
                }

                if (!realmNumbers.TryAdd(trait.realm, 1)) {
                    realmNumbers[trait.realm]++;
                }
            }
        }
        UIManager_Arena.Instance.Destinies.SetData(roleNumbers, realmNumbers);
    }

    public void Activate() {
        foreach (var (role, num) in roleNumbers) {
            var destiny = DestinyDB.Instance.Find(role);
            var index = destiny.GetCheckpointIndex(num);
            if (index >= 0) {
                var processor = DestinyProcessorFactory.Create(role);
                if (processor != null) {
                    var heroes = GameManager.Instance.BattleField.GetAliveHeroes(TeamSide.Ally);
                    processor.Activate(heroes, index);
                }
            }
        }
        
        foreach (var (realm, num) in realmNumbers) {
            var destiny = DestinyDB.Instance.Find(realm);
            var index = destiny.GetCheckpointIndex(num);
            if (index >= 0) {
                var processor = DestinyProcessorFactory.Create(realm);
                if (processor != null) {
                    var heroes = GameManager.Instance.BattleField.GetAliveHeroes(TeamSide.Ally);
                    processor.Activate(heroes, index);
                }
            }
        }
    }
}