namespace Transcendence.Data.Models.Tft.Match;

public enum TftFetchStatus
{
    Unfetched = 0,
    Success = 1,
    TemporaryFailure = 2,
    PermanentlyUnfetchable = 3,
    OutsideRetentionWindow = 4
}
