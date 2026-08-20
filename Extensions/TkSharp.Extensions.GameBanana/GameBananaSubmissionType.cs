namespace TkSharp.Extensions.GameBanana;

public enum GameBananaSubmissionType
{
    Mod,
    Wip
}

public static class GameBananaSubmissionTypeExtensions
{
    extension(GameBananaSubmissionType type)
    {
        public string ToApiName()
            => type switch {
                GameBananaSubmissionType.Wip => "Wip",
                _ => "Mod"
            };

        public string ToUrlSegment()
            => type switch {
                GameBananaSubmissionType.Wip => "wips",
                _ => "mods"
            };
    }

    public static GameBananaSubmissionType[] All => Enum.GetValues<GameBananaSubmissionType>();
}