using System;

public static class Translator {
    public static string ToName(this Realm realm) {
        return realm switch {
            Realm.Mortal => "Phàm nhân",
            Realm.Divine => "Thánh thần",
            Realm.Nether => "Âm ti",
            Realm.Mecha => "Cơ giới",
            Realm.Chaos => "Hỗn mang",
            _ => throw new ArgumentOutOfRangeException(nameof(realm), realm, null)
        };
    }

    public static string ToName(this Role role) {
        return role switch {
            Role.Duelist => "Kiếm sĩ",
            Role.Sorcerer => "Pháp sư",
            Role.Marksman => "Xạ thủ",
            Role.Assassin => "Thích khách",
            Role.Bruiser => "Quyền Vương",
            Role.Cultist => "Tín đồ",
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
        };
    }
    
    public static string ToName(this Reputation reputation) {
        return reputation switch {
            Reputation.Unknown => "Vô danh",
            Reputation.Elite => "Siêu việt",
            Reputation.Legendary => "Truyền thuyết",
            _ => throw new ArgumentOutOfRangeException(nameof(reputation), reputation, null)
        };
    }
}