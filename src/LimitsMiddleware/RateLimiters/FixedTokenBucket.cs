﻿namespace LimitsMiddleware.RateLimiters
{
    using System;
    using System.Threading;
    using LimitsMiddleware.Logging.LogProviders;

    internal class FixedTokenBucket
    {
        private readonly Func<int> _getBucketTokenCapacty;
        private readonly long _refillIntervalTicks;
        private readonly GetUtcNow _getUtcNow;
        private long _nextRefillTime;
        private long _tokens;
        private readonly InterlockedBoolean _updatingTokens = new InterlockedBoolean();
        private int _concurrentRequestCount;

        public FixedTokenBucket(
            Func<int> getBucketTokenCapacty,
            TimeSpan refillInterval,
            GetUtcNow getUtcNow = null)
        {
            _getBucketTokenCapacty = getBucketTokenCapacty;
            _refillIntervalTicks = refillInterval.Ticks;
            _getUtcNow = getUtcNow ?? SystemClock.GetUtcNow;
        }

        public bool ShouldThrottle(int tokenCount)
        {
            TimeSpan _;
            return ShouldThrottle(tokenCount, out _);
        }

        public bool ShouldThrottle(int tokenCount, out TimeSpan waitTimeSpan)
        {
            waitTimeSpan = TimeSpan.Zero;
            UpdateTokens();
            long tokens = Interlocked.Read(ref _tokens);
            if (tokens < tokenCount)
            {
                var currentTime = _getUtcNow().Ticks;
                var waitTicks = _nextRefillTime - currentTime;
                if (waitTicks < 0)
                {
                    return false;
                }
                waitTimeSpan = TimeSpan.FromTicks(waitTicks);
                return true;
            }
            Interlocked.Add(ref _tokens, -tokenCount);
            return false;
        }

        public long CurrentTokenCount
        {
            get
            {
                UpdateTokens();
                return Interlocked.Read(ref _tokens);
            }
        }

        public int Capacity
        {
            get { return _getBucketTokenCapacty(); }
        }

        public IDisposable RegisterRequest()
        {
            Interlocked.Increment(ref _concurrentRequestCount);
            return new DisposableAction(() =>
            {
                Interlocked.Decrement(ref _concurrentRequestCount);
            });
        }

        private void UpdateTokens()
        {
            if (_updatingTokens.EnsureCalledOnce())
            {
                return;
            }
            var currentTime = _getUtcNow().Ticks;

            if (currentTime >= _nextRefillTime)
            {
                Interlocked.Exchange(ref _tokens, _getBucketTokenCapacty());
                _nextRefillTime = currentTime + _refillIntervalTicks;
            }

            _updatingTokens.Set(false);
        }
    }
}