namespace AutoChess.Core {
    public enum CommandError {
        None,
        InvalidPlayer,
        WrongPhase,
        NotEnoughGold,
        BenchFull,
        BoardFull,
        InvalidSlot,
        InvalidUnit,
        InvalidLocation,
        InvalidItem,
        ItemSlotsFull,
        AlreadyChosen,
        MaxLevel,
        ShopSlotEmpty,
        CheatsDisabled,
        Unsupported,
    }

    public readonly struct CommandResult {
        public readonly CommandError Error;
        public readonly string Message;

        CommandResult(CommandError error, string message) {
            Error = error;
            Message = message;
        }

        public bool Ok => Error == CommandError.None;
        public static readonly CommandResult Success = new(CommandError.None, null);
        public static CommandResult Fail(CommandError error, string message = null) => new(error, message ?? error.ToString());
        public static implicit operator CommandResult(CommandError error) => error == CommandError.None ? Success : Fail(error);
        public override string ToString() => Ok ? "ok" : $"{Error}: {Message}";
    }
}
