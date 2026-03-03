namespace Transcendence.Service.Core.Services.Analysis.Exceptions;

public sealed class SummonerStatsComputationException : Exception
{
    public SummonerStatsComputationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
