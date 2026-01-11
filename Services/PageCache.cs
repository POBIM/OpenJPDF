// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Sittichat Pothising
// OpenJPDF - PDF Editor
// This file is part of OpenJPDF, licensed under AGPLv3.
// See LICENSE file for full license details.

using System.Windows.Media.Imaging;

namespace OpenJPDF.Services;

/// <summary>
/// LRU (Least Recently Used) cache for PDF page images.
/// Provides fast access to recently viewed pages while limiting memory usage.
/// </summary>
public class PageCache : IDisposable
{
    private readonly int _maxSize;
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _cacheMap;
    private readonly LinkedList<CacheEntry> _lruList;
    private readonly object _lock = new();
    private bool _disposed;

    private record CacheEntry(string Key, BitmapSource Image, DateTime CachedAt);

    /// <summary>
    /// Creates a new page cache with the specified maximum size.
    /// </summary>
    /// <param name="maxSize">Maximum number of pages to cache</param>
    public PageCache(int maxSize = 20)
    {
        _maxSize = maxSize;
        _cacheMap = new Dictionary<string, LinkedListNode<CacheEntry>>(maxSize);
        _lruList = new LinkedList<CacheEntry>();
    }

    /// <summary>
    /// Generate a cache key for a page with specific parameters.
    /// </summary>
    public static string GetCacheKey(int pageIndex, float scale, int rotation)
    {
        return $"page_{pageIndex}_scale_{scale:F2}_rot_{rotation}";
    }

    /// <summary>
    /// Generate a cache key for a thumbnail.
    /// </summary>
    public static string GetThumbnailKey(int pageIndex, int rotation)
    {
        return $"thumb_{pageIndex}_rot_{rotation}";
    }

    /// <summary>
    /// Try to get a cached page image.
    /// </summary>
    public bool TryGet(string key, out BitmapSource? image)
    {
        lock (_lock)
        {
            if (_cacheMap.TryGetValue(key, out var node))
            {
                // Move to front (most recently used)
                _lruList.Remove(node);
                _lruList.AddFirst(node);
                image = node.Value.Image;
                return true;
            }
        }

        image = null;
        return false;
    }

    /// <summary>
    /// Add or update a page image in the cache.
    /// </summary>
    public void Set(string key, BitmapSource image)
    {
        if (image == null) return;

        lock (_lock)
        {
            // If already exists, update and move to front
            if (_cacheMap.TryGetValue(key, out var existingNode))
            {
                _lruList.Remove(existingNode);
                _cacheMap.Remove(key);
            }

            // Evict oldest if at capacity
            while (_cacheMap.Count >= _maxSize && _lruList.Last != null)
            {
                var oldest = _lruList.Last;
                _lruList.RemoveLast();
                _cacheMap.Remove(oldest.Value.Key);
            }

            // Add new entry at front
            var entry = new CacheEntry(key, image, DateTime.UtcNow);
            var node = new LinkedListNode<CacheEntry>(entry);
            _lruList.AddFirst(node);
            _cacheMap[key] = node;
        }
    }

    /// <summary>
    /// Remove a specific page from cache.
    /// </summary>
    public void Remove(string key)
    {
        lock (_lock)
        {
            if (_cacheMap.TryGetValue(key, out var node))
            {
                _lruList.Remove(node);
                _cacheMap.Remove(key);
            }
        }
    }

    /// <summary>
    /// Remove all cached pages for a specific page index (all scales/rotations).
    /// </summary>
    public void InvalidatePage(int pageIndex)
    {
        lock (_lock)
        {
            var keysToRemove = _cacheMap.Keys
                .Where(k => k.Contains($"page_{pageIndex}_") || k.Contains($"thumb_{pageIndex}_"))
                .ToList();

            foreach (var key in keysToRemove)
            {
                if (_cacheMap.TryGetValue(key, out var node))
                {
                    _lruList.Remove(node);
                    _cacheMap.Remove(key);
                }
            }
        }
    }

    /// <summary>
    /// Clear all cached pages.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _cacheMap.Clear();
            _lruList.Clear();
        }
    }

    /// <summary>
    /// Get the current number of cached pages.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _cacheMap.Count;
            }
        }
    }

    /// <summary>
    /// Get cache statistics for debugging.
    /// </summary>
    public (int Count, int MaxSize, IEnumerable<string> Keys) GetStats()
    {
        lock (_lock)
        {
            return (Count, _maxSize, _cacheMap.Keys.ToList());
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Clear();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Thumbnail-specific cache with smaller default size.
/// </summary>
public class ThumbnailCache : PageCache
{
    public ThumbnailCache() : base(100) { } // Cache more thumbnails (they're smaller)
}
