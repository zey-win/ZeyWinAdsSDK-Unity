using System.Collections.Generic;
using ZeyWinAds.Ads;

namespace ZeyWinAds.Core
{
    /// <summary>
    /// Cache storage for preloaded ads.
    /// Stores one ad of each type for instant display.
    /// </summary>
    public class AdCache
    {
        private readonly Dictionary<AdType, BaseAd> _cache = new Dictionary<AdType, BaseAd>();
        private readonly object _lock = new object();

        /// <summary>
        /// Stores an ad in the cache for the specified type.
        /// Replaces any existing ad of the same type.
        /// </summary>
        /// <param name="type">The type of ad to store</param>
        /// <param name="ad">The ad instance to cache</param>
        public void Store(AdType type, BaseAd ad)
        {
            if (ad == null)
            {
                Logger.Warn("Attempted to store null ad in cache for type: {0}", type);
                return;
            }

            lock (_lock)
            {
                // Destroy existing ad if present
                if (_cache.TryGetValue(type, out BaseAd existingAd))
                {
                    Logger.Debug("Replacing existing cached ad for type: {0}", type);
                    existingAd?.Destroy();
                }

                _cache[type] = ad;
                Logger.Debug("Cached {0} ad: {1}", type, ad.AdData?.ad_id ?? "unknown");
            }
        }

        /// <summary>
        /// Retrieves a cached ad of the specified type.
        /// </summary>
        /// <param name="type">The type of ad to retrieve</param>
        /// <returns>The cached ad or null if not found</returns>
        public BaseAd Get(AdType type)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(type, out BaseAd ad))
                {
                    return ad;
                }
                return null;
            }
        }

        /// <summary>
        /// Retrieves and removes a cached ad of the specified type.
        /// </summary>
        /// <param name="type">The type of ad to retrieve</param>
        /// <returns>The cached ad or null if not found</returns>
        public BaseAd GetAndRemove(AdType type)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(type, out BaseAd ad))
                {
                    _cache.Remove(type);
                    Logger.Debug("Retrieved and removed cached {0} ad", type);
                    return ad;
                }
                return null;
            }
        }

        /// <summary>
        /// Checks if an ad of the specified type is cached and ready.
        /// </summary>
        /// <param name="type">The type of ad to check</param>
        /// <returns>True if a ready ad is cached</returns>
        public bool Has(AdType type)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(type, out BaseAd ad))
                {
                    return ad != null && ad.IsReady;
                }
                return false;
            }
        }

        /// <summary>
        /// Removes a cached ad of the specified type.
        /// </summary>
        /// <param name="type">The type of ad to remove</param>
        /// <returns>True if an ad was removed</returns>
        public bool Remove(AdType type)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(type, out BaseAd ad))
                {
                    ad?.Destroy();
                    _cache.Remove(type);
                    Logger.Debug("Removed cached {0} ad", type);
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Clears all cached ads and releases resources.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                foreach (var kvp in _cache)
                {
                    kvp.Value?.Destroy();
                }
                _cache.Clear();
                Logger.Debug("Cleared all cached ads");
            }
        }

        /// <summary>
        /// Gets the number of cached ads.
        /// </summary>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _cache.Count;
                }
            }
        }

        /// <summary>
        /// Gets the number of ready (loaded) ads in the cache.
        /// </summary>
        public int ReadyCount
        {
            get
            {
                lock (_lock)
                {
                    int count = 0;
                    foreach (var kvp in _cache)
                    {
                        if (kvp.Value != null && kvp.Value.IsReady)
                        {
                            count++;
                        }
                    }
                    return count;
                }
            }
        }
    }
}
