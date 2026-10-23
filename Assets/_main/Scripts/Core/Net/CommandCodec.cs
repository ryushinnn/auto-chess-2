using System;

namespace AutoChess.Core.Net {
    /// <summary>
    /// Compact binary encoding of <see cref="GameCommand"/>s: what a client sends to the server, and what a
    /// replay log stores. Layout: [type byte][player varint][fields...].
    /// </summary>
    public static class CommandCodec {
        public static byte[] Encode(GameCommand command) {
            var w = new NetWriter();
            Write(w, command);
            return w.ToArray();
        }

        public static GameCommand Decode(byte[] data) => Read(new NetReader(data));

        public static void Write(NetWriter w, GameCommand command) {
            w.WriteByte((byte)command.Type);
            w.WriteVarInt(command.PlayerId);
            switch (command) {
                case ChooseStarterCommand c:
                    w.WriteVarInt(c.OfferIndex);
                    break;
                case RerollStarterCommand c:
                    w.WriteVarInt(c.OfferIndex);
                    break;
                case BuyUnitCommand c:
                    w.WriteVarInt(c.ShopSlot);
                    break;
                case SetShopLockCommand c:
                    w.WriteBool(c.Locked);
                    break;
                case MoveUnitCommand c:
                    w.WriteVarInt(c.UnitId);
                    WriteLocation(w, c.Target);
                    break;
                case EquipItemCommand c:
                    w.WriteString(c.ItemId);
                    w.WriteVarInt(c.UnitId);
                    break;
                case SellUnitCommand c:
                    w.WriteVarInt(c.UnitId);
                    break;
                case CheatGoldCommand c:
                    w.WriteVarInt(c.Amount);
                    break;
                case CheatXpCommand c:
                    w.WriteVarInt(c.Amount);
                    break;
                case CheatItemCommand c:
                    w.WriteString(c.ItemId);
                    w.WriteVarInt(c.Count);
                    break;
                case CheatUnitCommand c:
                    w.WriteString(c.UnitId);
                    w.WriteByte((byte)c.Rank);
                    break;
                case CheatSetShopCommand c:
                    var ids = c.UnitIds ?? Array.Empty<string>();
                    w.WriteVarInt(ids.Length);
                    foreach (var id in ids) w.WriteString(id);
                    break;
                case RefreshShopCommand _:
                case BuyXpCommand _:
                case ReadyCommand _:
                case CheatSkipPhaseCommand _:
                    break;
                default:
                    throw new NotSupportedException($"no codec for {command.GetType().Name}");
            }
        }

        public static GameCommand Read(NetReader r) {
            var type = (CommandType)r.ReadByte();
            var player = r.ReadVarInt();
            GameCommand command = type switch {
                CommandType.ChooseStarter => new ChooseStarterCommand { OfferIndex = r.ReadVarInt() },
                CommandType.RerollStarter => new RerollStarterCommand { OfferIndex = r.ReadVarInt() },
                CommandType.BuyUnit => new BuyUnitCommand { ShopSlot = r.ReadVarInt() },
                CommandType.RefreshShop => new RefreshShopCommand(),
                CommandType.SetShopLock => new SetShopLockCommand { Locked = r.ReadBool() },
                CommandType.BuyXp => new BuyXpCommand(),
                CommandType.MoveUnit => new MoveUnitCommand { UnitId = r.ReadVarInt(), Target = ReadLocation(r) },
                CommandType.EquipItem => new EquipItemCommand { ItemId = r.ReadString(), UnitId = r.ReadVarInt() },
                CommandType.Ready => new ReadyCommand(),
                CommandType.SellUnit => new SellUnitCommand { UnitId = r.ReadVarInt() },
                CommandType.CheatGold => new CheatGoldCommand { Amount = r.ReadVarInt() },
                CommandType.CheatXp => new CheatXpCommand { Amount = r.ReadVarInt() },
                CommandType.CheatItem => new CheatItemCommand { ItemId = r.ReadString(), Count = r.ReadVarInt() },
                CommandType.CheatUnit => new CheatUnitCommand { UnitId = r.ReadString(), Rank = (HeroRank)r.ReadByte() },
                CommandType.CheatSkipPhase => new CheatSkipPhaseCommand(),
                CommandType.CheatSetShop => ReadSetShop(r),
                _ => throw new FormatException($"unknown command type {(int)type}")
            };
            command.PlayerId = player;
            return command;
        }

        static CheatSetShopCommand ReadSetShop(NetReader r) {
            var n = r.ReadVarInt();
            if (n < 0 || n > 64) throw new FormatException("bad shop size");
            var ids = new string[n];
            for (var i = 0; i < n; i++) ids[i] = r.ReadString();
            return new CheatSetShopCommand { UnitIds = ids };
        }

        static void WriteLocation(NetWriter w, UnitLocation location) {
            w.WriteByte((byte)location.Kind);
            switch (location.Kind) {
                case LocationKind.Bench:
                    w.WriteVarInt(location.Slot);
                    break;
                case LocationKind.Board:
                    w.WriteVarInt(location.Cell.Row);
                    w.WriteVarInt(location.Cell.Col);
                    break;
            }
        }

        static UnitLocation ReadLocation(NetReader r) {
            var kind = (LocationKind)r.ReadByte();
            return kind switch {
                LocationKind.Bench => UnitLocation.Bench(r.ReadVarInt()),
                LocationKind.Board => UnitLocation.Board(r.ReadVarInt(), r.ReadVarInt()),
                LocationKind.None => UnitLocation.None,
                _ => throw new FormatException($"bad location kind {(int)kind}")
            };
        }
    }
}
