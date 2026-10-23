using System.Collections.Generic;
using System.Linq;
using AutoChess.Core.Net;
using NUnit.Framework;

namespace AutoChess.Core.Tests {
    public class LockstepTests {
        /// <summary>A <see cref="LockstepHost"/> and its clients wired in memory, messages delivered at once.</summary>
        sealed class Room {
            public readonly LockstepHost Host;
            public readonly LockstepSession[] Clients;
            public readonly List<string> Errors = new();

            public Room(int players, bool cheats = true, ulong seed = 7, int hashInterval = 10) {
                var db = TestData.Database();
                var info = new LockstepStart {
                    MatchId = 1, Seed = seed, AllowCheats = cheats, SkipStarterPack = true, HashInterval = hashInterval,
                    Names = Enumerable.Range(0, players).Select(i => $"P{i}").ToArray()
                };
                Host = new LockstepHost(db, info, TestData.Abilities());
                Clients = new LockstepSession[players];
                for (var i = 0; i < players; i++) {
                    var player = i;
                    var start = Decode(Host.StartMessageFor(player));
                    var client = new LockstepSession(db, start, m => Host.Receive(player, m), TestData.Abilities());
                    client.ProtocolError += e => Errors.Add($"p{player}: {e}");
                    client.Start();
                    Clients[i] = client;
                }
                Host.Broadcast = m => {
                    foreach (var c in Clients) c.Receive(m);
                };
                Host.SendTo = (p, m) => Clients[p].Receive(m);
            }

            /// <summary>Server ticks <paramref name="frames"/> times; every client plays what it received.</summary>
            public void Run(int frames) {
                for (var i = 0; i < frames; i++) {
                    Host.Step();
                    foreach (var c in Clients) c.PlayAll();
                }
            }

            public void AssertInSync() {
                var expected = StateHasher.Hash(Host.Match.State);
                for (var i = 0; i < Clients.Length; i++) {
                    Assert.AreEqual(Host.Frame, Clients[i].Frame, $"client {i} frame");
                    Assert.AreEqual(expected, StateHasher.Hash(Clients[i].State), $"client {i} diverged at frame {Host.Frame}");
                    Assert.IsFalse(Clients[i].IsDesynced, $"client {i} flagged a desync");
                }
                CollectionAssert.IsEmpty(Errors);
            }
        }

        static LockstepStart Decode(byte[] message) {
            Assert.AreEqual(Lockstep.Start, message[0]);
            return LockstepStart.Read(new NetReader(message, 1, message.Length - 1));
        }

        [Test]
        public void EveryReplicaFollowsTheServerThroughARound() {
            var room = new Room(3);
            room.Clients[0].Submit(new CheatUnitCommand { UnitId = TestData.Knight });
            room.Clients[1].Submit(new CheatUnitCommand { UnitId = TestData.Archer });
            room.Clients[2].Submit(new CheatGoldCommand { Amount = 20 });
            room.Run(1);
            room.AssertInSync();
            room.Clients[2].Submit(new BuyUnitCommand { ShopSlot = 0 });
            foreach (var c in room.Clients) c.Submit(new ReadyCommand());
            room.Run(1);
            room.AssertInSync();

            var server = room.Host.Match;
            var limit = 600 * server.Rules.TicksPerSecond;
            for (var i = 0; i < limit && server.State.RoundIndex < 1; i++) room.Run(1);
            Assert.AreEqual(1, server.State.RoundIndex, "a full round was played");
            Assert.AreEqual(BattleOutcome.HomeWin, server.Player(0).LastOutcome);
            room.AssertInSync();
            Assert.AreEqual("P1", room.Clients[0].State.Player(1).Name);
        }

        [Test]
        public void ACommandTakesEffectWhenItsFrameIsPlayed() {
            var room = new Room(2);
            var gold = room.Clients[1].State.Player(0).Gold;
            CommandResult? result = null;
            room.Clients[0].Submit(new CheatGoldCommand { Amount = 10 }, r => result = r);
            Assert.IsNull(result, "nothing happens before the server relays it");
            Assert.AreEqual(gold, room.Clients[0].LocalPlayer.Gold);
            room.Run(1);
            Assert.IsTrue(result.HasValue && result.Value.Ok);
            Assert.AreEqual(gold + 10, room.Clients[0].LocalPlayer.Gold);
            Assert.AreEqual(gold + 10, room.Clients[1].State.Player(0).Gold, "the other replica applied it too");
            room.AssertInSync();
        }

        [Test]
        public void ARefusedCommandOnlyReachesItsSender() {
            var room = new Room(2);
            var seen = new List<int>();
            room.Host.Broadcast += m => {
                if (m[0] == Lockstep.Frame) seen.Add(LockstepFrame.Read(new NetReader(m, 1, m.Length - 1)).Entries.Count);
            };
            CommandResult? result = null;
            room.Clients[0].Submit(new MoveUnitCommand { UnitId = 999, Target = UnitLocation.Bench(0) }, r => result = r);
            room.Run(1);
            Assert.IsFalse(result?.Ok ?? true);
            CollectionAssert.AreEqual(new[] { 0 }, seen, "the refused command is not relayed");
            room.AssertInSync();
        }

        [Test]
        public void CheatsAreRefusedWhenTheMatchForbidsThem() {
            var room = new Room(1, cheats: false);
            CommandResult? result = null;
            room.Clients[0].Submit(new CheatGoldCommand { Amount = 50 }, r => result = r);
            Assert.AreEqual(CommandError.CheatsDisabled, result?.Error, "refused locally, not sent");

            // A modified client sends it anyway: the server refuses it too.
            var replies = new List<byte[]>();
            room.Host.SendTo = (p, m) => replies.Add(m);
            room.Host.Receive(0, Lockstep.EncodeCommand(5, new CheatGoldCommand { Amount = 50 }));
            room.Run(1);
            Assert.AreEqual(1, replies.Count);
            Assert.AreEqual(Lockstep.CommandRejected, replies[0][0]);
            room.AssertInSync();
        }

        [Test]
        public void TheServerDecidesWhoSentACommand() {
            var room = new Room(2);
            var gold0 = room.Host.Match.Player(0).Gold;
            var gold1 = room.Host.Match.Player(1).Gold;
            room.Host.Receive(1, Lockstep.EncodeCommand(1, new CheatGoldCommand { Amount = 30, PlayerId = 0 }));
            room.Run(1);
            Assert.AreEqual(gold0, room.Host.Match.Player(0).Gold, "player 1 cannot act as player 0");
            Assert.AreEqual(gold1 + 30, room.Host.Match.Player(1).Gold);
            room.AssertInSync();
        }

        [Test]
        public void ADivergedReplicaIsToldByTheServer() {
            var room = new Room(2, hashInterval: 5);
            var serverSaw = new List<int>();
            room.Host.Desynced += (player, frame, _, _) => serverSaw.Add(player);
            var clientSaw = new List<int>();
            room.Clients[1].Desynced += (frame, local, server) => {
                Assert.AreNotEqual(local, server);
                clientSaw.Add(frame);
            };
            room.Run(2);
            room.Clients[1].LocalPlayer.Gold += 7; // cheat engine
            room.Run(3);
            CollectionAssert.AreEqual(new[] { 1 }, serverSaw);
            CollectionAssert.AreEqual(new[] { 5 }, clientSaw);
            Assert.IsTrue(room.Clients[1].IsDesynced);
            Assert.IsFalse(room.Clients[0].IsDesynced);
        }

        [Test]
        public void PlaybackBuffersThenCatchesUp() {
            var room = new Room(1);
            var client = room.Clients[0];
            var dt = (float)client.Match.Rules.TickDelta;
            room.Host.Step();
            client.Advance(dt * 3);
            Assert.AreEqual(0, client.Frame, "waits for TargetBuffer frames");
            room.Host.Step();
            client.Advance(dt);
            Assert.AreEqual(1, client.Frame);
            for (var i = 0; i < 18; i++) room.Host.Step();
            client.Advance(0);
            Assert.AreEqual(9, client.Frame, "catching up, at most MaxTicksPerAdvance per call");
            client.Advance(0);
            Assert.AreEqual(14, client.Frame, "down to MaxBuffer");
            client.Advance(0);
            Assert.AreEqual(14, client.Frame);
            client.Advance(dt);
            Assert.AreEqual(15, client.Frame);
            client.PlayAll();
            room.AssertInSync();
        }

        [Test]
        public void EightBotsPlayAWholeMatchThatReplaysExactly() {
            var db = TestData.Database();
            var info = new LockstepStart {
                MatchId = 2, Seed = 99, HashInterval = 0, SetupSeconds = 5, PreparationSeconds = 10,
                Names = Enumerable.Range(0, 8).Select(i => $"Bot {i}").ToArray()
            };
            var host = new LockstepHost(db, info, TestData.Abilities());
            var brains = Enumerable.Range(0, 8).Select(i => new AI.BotBrain(i, 99)).ToList();
            var commands = 0;
            for (var frame = 0; frame < 30 * 60 * 30 && !host.IsFinished; frame++) {
                foreach (var brain in brains) {
                    var command = brain.Think(host.Match.State, db, (unit, target) => host.Match.CanMove(brain.PlayerId, unit, target));
                    if (command == null) continue;
                    host.Submit(brain.PlayerId, command);
                    commands++;
                }
                host.Step();
            }
            Assert.IsTrue(host.IsFinished, $"stuck in {host.Match.Phase} at frame {host.Frame}");
            Assert.Greater(commands, 8 * 3);
            Assert.IsTrue(host.Match.State.Players.All(p => p.StarterChosen && p.Units.Count > 0));
            var replay = MatchRecorder.Replay(db, host.Recorder.Recording, TestData.Abilities());
            Assert.AreEqual(StateHasher.Hash(host.Match.State), StateHasher.Hash(replay.State));
        }

        [Test]
        public void MessagesRoundTrip() {
            var start = new LockstepStart {
                MatchId = 42, Seed = 0xDEADBEEF12345678UL, TickRate = 30, HashInterval = 15, AllowCheats = true,
                LocalPlayer = 1, Names = new[] { "An", "Bình" }
            };
            var back = Decode(start.Encode());
            Assert.AreEqual(start.Seed, back.Seed);
            Assert.AreEqual(1, back.LocalPlayer);
            Assert.IsTrue(back.AllowCheats);
            Assert.IsFalse(back.SkipStarterPack);
            CollectionAssert.AreEqual(start.Names, back.Names);

            var frame = new LockstepFrame { Number = 300 };
            frame.Entries.Add(new LockstepFrame.Entry { Seq = 3, Command = new MoveUnitCommand { PlayerId = 1, UnitId = 9, Target = UnitLocation.Board(2, 4) } });
            frame.Entries.Add(new LockstepFrame.Entry { Seq = 4, Command = new ReadyCommand { PlayerId = 0 } });
            var bytes = frame.Encode();
            var read = LockstepFrame.Read(new NetReader(bytes, 1, bytes.Length - 1));
            Assert.AreEqual(300, read.Number);
            Assert.AreEqual(2, read.Entries.Count);
            var move = (MoveUnitCommand)read.Entries[0].Command;
            Assert.AreEqual((1, 9, 3), (move.PlayerId, move.UnitId, read.Entries[0].Seq));
            Assert.AreEqual(UnitLocation.Board(2, 4), move.Target);

            Assert.AreEqual(Lockstep.DataHash("{\n\"a\": 1\n}"), Lockstep.DataHash("{\r\n\"a\": 1\r\n}\r\n"));
            Assert.AreNotEqual(Lockstep.DataHash("{\"a\": 1}"), Lockstep.DataHash("{\"a\": 2}"));
        }
    }
}
