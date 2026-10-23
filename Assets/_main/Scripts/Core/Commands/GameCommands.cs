namespace AutoChess.Core {
    public enum CommandType : byte {
        ChooseStarter = 1,
        RerollStarter = 2,
        BuyUnit = 3,
        RefreshShop = 4,
        SetShopLock = 5,
        BuyXp = 6,
        MoveUnit = 7,
        EquipItem = 8,
        Ready = 9,
        SellUnit = 10,

        // Development / test only (rejected unless MatchConfig.AllowCheats).
        CheatGold = 100,
        CheatXp = 101,
        CheatItem = 102,
        CheatUnit = 103,
        CheatSkipPhase = 104,
        CheatSetShop = 105,
    }

    /// <summary>
    /// A player's intent. Commands are the only way to change a match from outside: the local client applies
    /// them directly, a networked client sends them to the server (see CommandCodec), and tests script them.
    /// </summary>
    public abstract class GameCommand {
        /// <summary>Issuing player; -1 = "the local player" (filled in by the session).</summary>
        public int PlayerId = -1;
        public abstract CommandType Type { get; }
        public virtual bool IsCheat => false;
        public override string ToString() => $"{Type}(p{PlayerId})";
    }

    public sealed class ChooseStarterCommand : GameCommand {
        public int OfferIndex;
        public override CommandType Type => CommandType.ChooseStarter;
    }

    public sealed class RerollStarterCommand : GameCommand {
        public int OfferIndex;
        public override CommandType Type => CommandType.RerollStarter;
    }

    public sealed class BuyUnitCommand : GameCommand {
        public int ShopSlot;
        public override CommandType Type => CommandType.BuyUnit;
    }

    public sealed class RefreshShopCommand : GameCommand {
        public override CommandType Type => CommandType.RefreshShop;
    }

    public sealed class SetShopLockCommand : GameCommand {
        public bool Locked;
        public override CommandType Type => CommandType.SetShopLock;
    }

    /// <summary>Sells an owned unit (bench any time, board during preparation); its items go back to the item bench.</summary>
    public sealed class SellUnitCommand : GameCommand {
        public int UnitId;
        public override CommandType Type => CommandType.SellUnit;
    }

    public sealed class BuyXpCommand : GameCommand {
        public override CommandType Type => CommandType.BuyXp;
    }

    public sealed class MoveUnitCommand : GameCommand {
        public int UnitId;
        public UnitLocation Target;
        public override CommandType Type => CommandType.MoveUnit;
    }

    public sealed class EquipItemCommand : GameCommand {
        public string ItemId;
        public int UnitId;
        public override CommandType Type => CommandType.EquipItem;
    }

    /// <summary>The player is done preparing. When every player is ready the phase ends shortly after.</summary>
    public sealed class ReadyCommand : GameCommand {
        public override CommandType Type => CommandType.Ready;
    }

    public sealed class CheatGoldCommand : GameCommand {
        public int Amount;
        public override CommandType Type => CommandType.CheatGold;
        public override bool IsCheat => true;
    }

    public sealed class CheatXpCommand : GameCommand {
        public int Amount;
        public override CommandType Type => CommandType.CheatXp;
        public override bool IsCheat => true;
    }

    public sealed class CheatItemCommand : GameCommand {
        public string ItemId;
        public int Count = 1;
        public override CommandType Type => CommandType.CheatItem;
        public override bool IsCheat => true;
    }

    public sealed class CheatUnitCommand : GameCommand {
        public string UnitId;
        public HeroRank Rank;
        public override CommandType Type => CommandType.CheatUnit;
        public override bool IsCheat => true;
    }

    /// <summary>Shortens the current phase (the old Space shortcut).</summary>
    public sealed class CheatSkipPhaseCommand : GameCommand {
        public override CommandType Type => CommandType.CheatSkipPhase;
        public override bool IsCheat => true;
    }

    /// <summary>Replaces the shop offer (null entries = empty slots). Makes scripted tests independent of luck.</summary>
    public sealed class CheatSetShopCommand : GameCommand {
        public string[] UnitIds;
        public override CommandType Type => CommandType.CheatSetShop;
        public override bool IsCheat => true;
    }
}
