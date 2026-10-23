using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using AutoChess.Core;
using AutoChess.Core.Net;

namespace AutoChess.Net {
    public enum OnlineState {
        Offline,
        Connecting,
        /// <summary>Connected and accepted (Welcome received); not queued, not in a match.</summary>
        Lobby,
        Queued,
        InMatch,
    }

    /// <summary>
    /// Client side of the auto-chess-2-server protocol (<see cref="Lockstep"/>) over a WebSocket. Socket I/O
    /// runs on background tasks; everything observable (state, events, the <see cref="LockstepSession"/>)
    /// changes only inside <see cref="Update"/>, which the owner calls from its main thread (Unity's Update,
    /// a bot loop). Engine-free: the Unity client and the server's bots share it.
    /// </summary>
    public sealed class OnlineClient : IDisposable {
        readonly GameDatabase db;
        readonly ulong dataHash;
        readonly Core.Combat.AbilityRegistry abilities;
        Link link;

        public OnlineClient(GameDatabase db, ulong dataHash, Core.Combat.AbilityRegistry abilities = null) {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
            this.dataHash = dataHash;
            this.abilities = abilities;
        }

        public OnlineState State { get; private set; } = OnlineState.Offline;
        /// <summary>Last thing worth showing to the player (connection error, refusal, desync...).</summary>
        public string Status { get; private set; } = "";
        public int ClientId { get; private set; }
        public int QueueWaiting { get; private set; }
        public int MatchSize { get; private set; }
        /// <summary>Replica of the current (or last) match; kept after it ends until <see cref="LeaveMatch"/>.</summary>
        public LockstepSession Session { get; private set; }

        public event Action<LockstepSession> MatchStarted;
        /// <summary>The connection is gone (reason); <see cref="State"/> is Offline again.</summary>
        public event Action<string> Disconnected;
        public event Action<string> Log;

        // ------------------------------------------------------------------ commands (main thread)

        public void Connect(string url, string playerName, string deviceId) {
            if (State != OnlineState.Offline) return;
            State = OnlineState.Connecting;
            Status = $"connecting to {url}";
            link = new Link();
            link.Send(Lockstep.EncodeHello(dataHash, playerName, deviceId));
            var current = link;
            Task.Run(() => current.RunAsync(new Uri(url)));
        }

        public void FindMatch() {
            if (State != OnlineState.Lobby) return;
            Session = null;
            Send(Lockstep.Empty(Lockstep.FindMatch));
            State = OnlineState.Queued;
            Status = "looking for a match";
        }

        public void CancelFind() {
            if (State != OnlineState.Queued) return;
            Send(Lockstep.Empty(Lockstep.CancelFind));
            State = OnlineState.Lobby;
            Status = "";
        }

        /// <summary>Leaves the current match (the server keeps simulating our roster) and drops the replica.</summary>
        public void LeaveMatch() {
            if (State == OnlineState.InMatch) Send(Lockstep.Empty(Lockstep.LeaveMatch));
            if (State == OnlineState.InMatch) State = OnlineState.Lobby;
            Session = null;
        }

        public void Disconnect() {
            if (State == OnlineState.Offline) return;
            link?.Close("disconnected");
        }

        public void Dispose() {
            Disconnect();
            Drop("disposed");
        }

        void Send(byte[] message) => link?.Send(message);

        // ------------------------------------------------------------------ main-thread pump

        /// <summary>Handles everything received since the last call. Call it every frame.</summary>
        public void Update() {
            var current = link;
            if (current == null) return;
            while (current.Inbox.TryDequeue(out var message)) Handle(message);
            if (current.Done && ReferenceEquals(current, link)) {
                while (current.Inbox.TryDequeue(out var message)) Handle(message);
                Drop(current.CloseReason ?? "connection closed");
            }
        }

        void Handle(byte[] message) {
            if (message.Length == 0) return;
            try {
                var r = new NetReader(message, 1, message.Length - 1);
                switch (message[0]) {
                    case Lockstep.Welcome:
                        ClientId = r.ReadVarInt();
                        State = OnlineState.Lobby;
                        Status = "connected";
                        break;
                    case Lockstep.Refused: {
                        var reason = (Lockstep.RefuseReason)r.ReadVarInt();
                        var text = r.ReadString();
                        link?.Close($"refused ({reason}): {text}");
                        break;
                    }
                    case Lockstep.Queue:
                        QueueWaiting = r.ReadVarInt();
                        MatchSize = r.ReadVarInt();
                        if (State == OnlineState.Queued) Status = $"looking for a match ({QueueWaiting} waiting, up to {MatchSize})";
                        break;
                    case Lockstep.Start: {
                        var start = LockstepStart.Read(r);
                        Session = new LockstepSession(db, start, Send, abilities);
                        Session.Desynced += (frame, local, server) =>
                            Report($"desync at frame {frame}: local {local:x16}, server {server:x16}");
                        Session.ProtocolError += Report;
                        Session.Ended += reason => {
                            if (State == OnlineState.InMatch) State = OnlineState.Lobby;
                            Status = reason == Lockstep.EndReason.Finished ? "match finished" : "match aborted";
                        };
                        State = OnlineState.InMatch;
                        Status = $"match {start.MatchId}: {start.PlayerCount} players";
                        MatchStarted?.Invoke(Session);
                        break;
                    }
                    default:
                        Session?.Receive(message);
                        break;
                }
            }
            catch (FormatException e) {
                Report($"bad message {message[0]}: {e.Message}");
            }
        }

        void Report(string text) {
            Status = text;
            Log?.Invoke(text);
        }

        void Drop(string reason) {
            link = null;
            if (State == OnlineState.Offline) return;
            var session = Session;
            if (session != null && !session.IsEnded) session.Receive(Lockstep.Ints(Lockstep.End, (int)Lockstep.EndReason.Aborted));
            State = OnlineState.Offline;
            Status = reason;
            Log?.Invoke($"disconnected: {reason}");
            Disconnected?.Invoke(reason);
        }

        // ------------------------------------------------------------------ socket tasks

        /// <summary>One connection: its socket, queues and tasks (a reconnect gets a fresh one).</summary>
        sealed class Link {
            public readonly ConcurrentQueue<byte[]> Inbox = new();
            readonly ConcurrentQueue<byte[]> outbox = new();
            readonly SemaphoreSlim outboxSignal = new(0);
            readonly CancellationTokenSource cancel = new();
            volatile string closeReason;
            volatile bool done;

            public string CloseReason => closeReason;
            /// <summary>Both socket tasks ended; nothing more will arrive.</summary>
            public bool Done => done;

            public void Send(byte[] message) {
                outbox.Enqueue(message);
                outboxSignal.Release();
            }

            public void Close(string reason) {
                closeReason ??= reason;
                cancel.Cancel();
            }

            public async Task RunAsync(Uri uri) {
                var ws = new ClientWebSocket();
                var token = cancel.Token;
                try {
                    await ws.ConnectAsync(uri, token).ConfigureAwait(false);
                    var sending = SendLoopAsync(ws, token);
                    await ReceiveLoopAsync(ws, token).ConfigureAwait(false);
                    cancel.Cancel();
                    await sending.ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (Exception e) {
                    closeReason ??= e is WebSocketException ? $"connection failed: {e.Message}" : $"{e.GetType().Name}: {e.Message}";
                }
                finally {
                    closeReason ??= "connection closed";
                    try {
                        if (ws.State == WebSocketState.Open) {
                            using var timeout = new CancellationTokenSource(1000);
                            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", timeout.Token).ConfigureAwait(false);
                        }
                    }
                    catch (Exception) { }
                    ws.Dispose();
                    done = true;
                }
            }

            async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken token) {
                var buffer = new byte[8192];
                using var message = new MemoryStream();
                while (!token.IsCancellationRequested && ws.State == WebSocketState.Open) {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close) {
                        closeReason ??= string.IsNullOrEmpty(result.CloseStatusDescription) ? "server closed the connection" : result.CloseStatusDescription;
                        return;
                    }
                    message.Write(buffer, 0, result.Count);
                    if (message.Length > 1 << 20) throw new InvalidDataException("message too large");
                    if (!result.EndOfMessage) continue;
                    Inbox.Enqueue(message.ToArray());
                    message.SetLength(0);
                }
            }

            async Task SendLoopAsync(ClientWebSocket ws, CancellationToken token) {
                try {
                    while (true) {
                        while (outbox.TryDequeue(out var message)) {
                            await ws.SendAsync(new ArraySegment<byte>(message), WebSocketMessageType.Binary, true, token).ConfigureAwait(false);
                        }
                        await outboxSignal.WaitAsync(token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception e) {
                    closeReason ??= $"send failed: {e.Message}";
                    cancel.Cancel();
                }
            }
        }
    }
}
