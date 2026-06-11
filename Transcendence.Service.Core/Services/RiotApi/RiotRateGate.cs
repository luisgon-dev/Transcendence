using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Transcendence.Service.Core.Services.Jobs.Configuration;

namespace Transcendence.Service.Core.Services.RiotApi;

public interface IRiotRateGate
{
    /// <summary>
    /// Waits for a request token for the given Riot routing region (subdomain), returning true once one is
    /// available (proceed with the call). Returns false if no token becomes available within the configured
    /// max wait — the caller should skip the call and let it be retried later, rather than block forever.
    /// </summary>
    Task<bool> AcquireAsync(string routingKey, CancellationToken ct = default);
}

/// <summary>
/// A per-routing-region token-bucket gate that paces outbound Riot requests to stay UNDER the key's
/// per-region budget, so Camille's own rate limiter never has to queue/park requests (the saturation that
/// stalled ingestion). Dependency-free: a <see cref="SemaphoreSlim"/> of tokens per region, refilled by a
/// timer at the sustained rate. Each region's bucket is independent, so the regions' budgets are used in
/// parallel. Registered as a singleton; injected into the Riot client wrappers.
/// </summary>
public sealed class RiotRateGate(IOptions<RiotRateGateOptions> options) : IRiotRateGate, IDisposable
{
    private readonly RiotRateGateOptions _options = options.Value;
    private readonly ConcurrentDictionary<string, Bucket> _buckets = new(StringComparer.OrdinalIgnoreCase);

    public Task<bool> AcquireAsync(string routingKey, CancellationToken ct = default)
    {
        if (!_options.Enabled)
            return Task.FromResult(true);

        var key = string.IsNullOrWhiteSpace(routingKey) ? "default" : routingKey.Trim().ToUpperInvariant();
        return _buckets.GetOrAdd(key, _ => new Bucket(_options)).AcquireAsync(ct);
    }

    public void Dispose()
    {
        foreach (var bucket in _buckets.Values)
            bucket.Dispose();
    }

    private sealed class Bucket : IDisposable
    {
        private readonly SemaphoreSlim _tokens;
        private readonly Timer _refillTimer;
        private readonly int _maxTokens;
        private readonly int _tokensPerPeriod;
        private readonly int _maxWaitMs;
        private readonly object _refillLock = new();

        public Bucket(RiotRateGateOptions o)
        {
            _maxTokens = Math.Max(1, o.BurstTokens);
            _tokensPerPeriod = Math.Max(1, o.TokensPerPeriod);
            _maxWaitMs = Math.Max(1000, o.MaxWaitSeconds * 1000);
            _tokens = new SemaphoreSlim(_maxTokens, _maxTokens);
            var periodMs = Math.Max(250, o.ReplenishPeriodSeconds * 1000);
            _refillTimer = new Timer(_ => Refill(), null, periodMs, periodMs);
        }

        private void Refill()
        {
            // Release up to _tokensPerPeriod, but never above the bucket capacity. CurrentCount can only
            // drop (acquisitions) between the read and the release, and refills are serialized by the lock,
            // so the released amount can never push the semaphore over _maxTokens.
            lock (_refillLock)
            {
                var room = _maxTokens - _tokens.CurrentCount;
                var release = Math.Min(_tokensPerPeriod, room);
                if (release > 0)
                    _tokens.Release(release);
            }
        }

        public async Task<bool> AcquireAsync(CancellationToken ct)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_maxWaitMs);
            try
            {
                await _tokens.WaitAsync(timeoutCts.Token);
                return true;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Token did not become available within the max wait — reject so the caller skips this call
                // instead of holding a worker slot indefinitely.
                return false;
            }
        }

        public void Dispose()
        {
            _refillTimer.Dispose();
            _tokens.Dispose();
        }
    }
}
