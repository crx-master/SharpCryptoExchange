using SharpCryptoExchange.Interfaces;
using SharpCryptoExchange.Objects;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SharpCryptoExchange
{
    /// <summary>
    /// Base rest API client for interacting with a REST API
    /// </summary>
    public abstract class RestApiClient : BaseApiClient
    {
        /// <summary>
        /// Get time sync info for an API client
        /// </summary>
        /// <returns></returns>
        public abstract TimeSyncInfo GetTimeSyncInfo();

        /// <summary>
        /// Get time offset for an API client
        /// </summary>
        /// <returns></returns>
        public abstract TimeSpan GetTimeOffset();

        /// <summary>
        /// Total amount of requests made with this API client
        /// </summary>
        public int TotalRequestsMade { get; set; }

        /// <summary>
        /// Options for this client
        /// </summary>
        public new RestApiClientOptions Options => (RestApiClientOptions)base.Options;

        /// <summary>
        /// List of rate limiters
        /// </summary>
        internal IEnumerable<IRateLimiter> RateLimiters { get; }

        /// <summary>
        /// ctor
        /// </summary>
        /// <param name="options">The base client options</param>
        /// <param name="apiOptions">The Api client options</param>
        public RestApiClient(BaseRestClientOptions options, RestApiClientOptions apiOptions) : base(options, apiOptions)
        {
            var rateLimiters = new List<IRateLimiter>();
            foreach (var rateLimiter in apiOptions.RateLimiters)
                rateLimiters.Add(rateLimiter);
            RateLimiters = rateLimiters;
        }

        /// <summary>
        /// Retrieve the server time for the purpose of syncing time between client and server to prevent authentication issues
        /// </summary>
        /// <returns>Server time</returns>
        protected abstract Task<WebCallResult<DateTimeOffset>> GetServerTimestampAsync();

        internal async Task<WebCallResult<bool>> SyncTimeAsync()
        {
            var timeSyncParams = GetTimeSyncInfo();
            if (await timeSyncParams.TimeSyncState.Semaphore.WaitAsync(0).ConfigureAwait(false))
            {
                try
                {
                    if (!timeSyncParams.SyncTime || (DateTime.UtcNow - timeSyncParams.TimeSyncState.LastSyncTime < timeSyncParams.RecalculationInterval))
                        return new WebCallResult<bool>(null, null, null, null, null, null, null, null, true, null);

                    var localTime = DateTime.UtcNow;
                    var result = await GetServerTimestampAsync().ConfigureAwait(false);
                    if (!result) return result.As(false);

                    if (TotalRequestsMade == 1)
                    {
                        // If this was the first request make another one to calculate the offset since the first one can be slower
                        localTime = DateTime.UtcNow;
                        result = await GetServerTimestampAsync().ConfigureAwait(false);
                        if (!result) return result.As(false);
                    }

                    // Calculate time offset between local and server
                    var offset = result.Data - (localTime.AddMilliseconds(result.ResponseTime!.Value.TotalMilliseconds / 2));
                    timeSyncParams.UpdateTimeOffset(offset);
                }
                finally
                {
                    timeSyncParams.TimeSyncState.Semaphore.Release();
                }
            }

            return new WebCallResult<bool>(null, null, null, null, null, null, null, null, true, null);
        }
    }
}
