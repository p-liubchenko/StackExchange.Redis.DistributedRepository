using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using StackExchange.Redis.DistributedRepository.Models;
using StackExchange.Redis.DistributedRepository.Telemetry;
using static StackExchange.Redis.DistributedRepository.Extensions.RepositoryExtensions;

[assembly: InternalsVisibleTo("StackExchange.Redis.DistributedRepository.Banchmark")]
namespace StackExchange.Redis.DistributedRepository;

public class DistributedBackedRepository<T> : DistributedRepository<T> where T : class
{
	/// <summary>
	/// Base key in memory full list
	/// </summary>
	protected string MemoryListKey
	{
		get => $"{BaseKey}:list";
	}

	private readonly IMemoryCache _memoryCache;
	private readonly ConcurrentDictionary<string, T> _memDict = new();

	public DistributedBackedRepository(
		IConnectionMultiplexer redis,
		IMemoryCache memoryCache,
		Func<T, string> keySelector,
		IRepositoryMetrics? metrics = null,
		ILogger<DistributedBackedRepository<T>>? logger = null,
		IEnumerable<RedisIndexer<T>>? indexers = null,
		string? keyPrefix = null) : base(redis, keySelector, metrics, logger, indexers, keyPrefix: keyPrefix)
	{

		_memoryCache = memoryCache;
		_memoryCache.Set(MemoryListKey, _memDict);
		Task.Run(RebakeAll).ConfigureAwait(true);
	}

	#region add
	public override T Add(T item) => AddAsync(item).GetAwaiter().GetResult();

	public override async Task<T> AddAsync(T item)
	{
		ArgumentNullException.ThrowIfNull(item);
		string key = KeySelector.Invoke(item);

		if (string.IsNullOrEmpty(key))
			throw new ArgumentNullException(nameof(item), "KeySelector cannot return null or empty string");

		using Activity? activity = ActivitySourceProvider.StartActivity("repo.add.redis", key: key);
		Stopwatch? sw = Stopwatch.StartNew();

		try
		{
			T result = await AddRedis(key, item);
			MemoryAdd(item);
			await _bus.PublishAsync(_busChannel, GenerateMessage(MessageType.Created, key));
			return result;
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "Error adding item to repository");
			throw;
		}
		finally
		{
			sw.Stop();
			_metrics?.ObserveDuration("repo.add", sw.Elapsed);
		}
	}

	public new void AddRange(IEnumerable<T> range) =>
		AddRangeAsync(range).GetAwaiter().GetResult();

	public override async Task AddRangeAsync(IEnumerable<T> range)
	{
		await RedisAddRange(range);
		MemoryAddRange(range);
	}

	protected void MemoryAddRange(IEnumerable<T> range)
	{
		foreach (var item in range)
		{
			_memDict[KeySelector.Invoke(item)] = item;
		}
	}

	protected void MemoryAdd(T? item)
	{
		if (item is null)
			return;

		_memDict[KeySelector.Invoke(item)] = item;
	}
	#endregion

	#region remove
	public override T? Remove(T item)
	{
		string? key = KeySelector.Invoke(item);
		return Remove(key);
	}

	public override async Task<T?> RemoveAsync(T item)
	{
		string? key = KeySelector.Invoke(item);
		return await RemoveAsync(key);
	}

	public override T? Remove(string key) => RemoveAsync(key).GetAwaiter().GetResult();

	public override async Task<T?> RemoveAsync(string key)
	{
		var poped = await GetAsync(key);
		if (poped is null)
			return null;
		await RemoveRedis(poped);
		RemoveMemory(key);
		_bus.Publish(_busChannel, GenerateMessage(MessageType.Deleted, key));
		return poped;
	}

	protected void RemoveMemory(string key)
	{
		_memDict.Remove(key, out _);
	}
	#endregion

	/// <inheritdoc/>
	public override T? Get(string Key)
	{
		using Activity? activity = ActivitySourceProvider.StartActivity("repo.get", key: Key);
		Stopwatch? sw = Stopwatch.StartNew();
		try
		{
			if (_memDict.TryGetValue(Key, out T? itemMemory))
				return itemMemory;

			if (_database.HashExists(BaseKey, Key))
			{
				string? value = _database.HashGet(BaseKey, Key);
				if (string.IsNullOrEmpty(value))
					return null;
				T item = JsonSerializer.Deserialize<T?>(value);
				MemoryAdd(item);
				return item;
			}
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "Error getting item from repository");
			throw;
		}
		finally
		{
			_metrics?.ObserveDuration("repo.get", sw.Elapsed);
			activity?.Stop();
		}

		return null;
	}

	/// <inheritdoc/>
	public override T GetOrAdd(string key, Func<T> factory)
	{
		T? item = Get(key);
		if (item is not null)
			return item;
		item = factory();
		Add(item);
		return item;
	}

	/// <inheritdoc/>
	public override async Task<T?> GetAsync(string? key)
	{
		if (key is null)
			return null;
		using Activity? activity = ActivitySourceProvider.StartActivity("repo.get", key: key);
		Stopwatch? sw = Stopwatch.StartNew();
		try
		{
			if (_memDict.TryGetValue(key, out T? itemMemory))
				return itemMemory;

			string? value = await _database.HashGetAsync(BaseKey, key);
			if (string.IsNullOrEmpty(value))
				return null;
			T? item = JsonSerializer.Deserialize<T>(value);
			MemoryAdd(item);
			return item;

		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "Error getting item from repository");
			throw;
		}
		finally
		{
			_metrics?.ObserveDuration("repo.get", sw.Elapsed);
			activity?.Stop();
		}

		return null;
	}

	/// <inheritdoc/>
	public override async Task<T> GetOrAddAsync(string key, Func<Task<T>> factory)
	{
		T? item = await GetAsync(key);
		if (item is not null)
			return item;
		item = await factory();
		await AddAsync(item);
		return item;
	}

	/// <inheritdoc/>
	public override async Task<IEnumerable<T>> GetAsync()
	{
		if (_memDict.Values.Any())
		{
			return _memDict.Values;
		}
		IEnumerable<T>? values = await base.GetAsync();

		MemoryAddRange(values);

		return values;
	}

	/// <inheritdoc/>
	public override IEnumerable<T> Get()
	{
		if (_memDict.Values.Any())
			return _memDict.Values;

		IEnumerable<T> values = base.Get();

		_memoryCache.Set(
			MemoryListKey,
			new ConcurrentDictionary<string, T>(values.ToDictionary(x => KeySelector.Invoke(x!), x => x))
		);

		return values.Where(x => x is not null)?? Enumerable.Empty<T>();

	}

	protected bool MemoryTryGet(string key, T? item = null)
	{
		return _memDict.TryGetValue(key, out item);
	}

	/// <inheritdoc/>
	public async Task RebakeAll()
	{
		using Activity? activity = ActivitySourceProvider.StartActivity("repo.rebake");
		Stopwatch? sw = Stopwatch.StartNew();
		try
		{

			RedisValue[]? keys = _database.SetMembers(BaseKeyTracker);

			foreach (var item in keys)
			{
				MemoryAdd(await GetAsync(item));
			}
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "Error rebaking all items in repository");
			throw;
		}
		finally
		{
			_metrics?.ObserveDuration("repo.rebake", sw.Elapsed);
			activity?.Stop();
		}
	}

	/// <inheritdoc/>
	public override async Task Purge()
	{
		using Activity? activity = ActivitySourceProvider.StartActivity("repo.purge");
		Stopwatch? sw = Stopwatch.StartNew();
		try
		{
			await base.Purge();
			await _bus.PublishAsync(_busChannel, GenerateMessage(MessageType.Purged, null));
			MemoryPurge();
			await RebakeAll();
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "Error purging all items in repository");
			throw;
		}
		finally
		{
			_metrics?.ObserveDuration("repo.purge", sw.Elapsed);
			activity?.Stop();
		}
	}

	public void MemoryPurge()
	{
		_memDict.Clear();
	}

	/// <inheritdoc/>
	public override async Task Rebuild()
	{
		using Activity? activity = ActivitySourceProvider.StartActivity("repo.rebuild");
		Stopwatch? sw = Stopwatch.StartNew();
		try
		{
			RedisValue[]? keys = _database.HashKeys(BaseKey);
			RedisValue[]? tracked = _database.SetMembers(BaseKeyTracker);

			IEnumerable<RedisValue>? toRemove = tracked.Except(keys);
			IEnumerable<RedisValue>? toAdd = keys.Except(tracked);

			ITransaction? transaction = _database.CreateTransaction();

			transaction.SetRemoveAsync(BaseKeyTracker, toRemove.ToArray());
			transaction.SetAddAsync(BaseKeyTracker, toAdd.ToArray());
			await transaction.ExecuteAsync();

			foreach (RedisValue key in keys)
			{
				MemoryAdd(await GetAsync(key));
			}
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "Error rebaking all items in repository");
			throw;
		}
		finally
		{
			_metrics?.ObserveDuration("repo.rebuild", sw.Elapsed);
			activity?.Stop();
		}
	}

	protected override void ItemUpdatedHandler(RedisChannel channel, RedisValue value)
	{
		Message? message = JsonSerializer.Deserialize<Message>(value.ToString());

		if (message is null)
			return;
		if (message.i == _instanceId)
			return;

		switch (message.type)
		{
			case MessageType.Created:
				if (string.IsNullOrEmpty(message.item))
					return;
				T? created = Get(message.item);
				if (created is null) return;
				MemoryAdd(created);
				break;
			case MessageType.Updated:
				if (string.IsNullOrEmpty(message.item))
					return;
				T? updated = Get(message.item);
				if (updated is null) return;
				MemoryAdd(updated);
				break;
			case MessageType.Deleted:
				if (string.IsNullOrEmpty(message.item))
					return;
				RemoveMemory(message.item);
				break;
			case MessageType.Purged:
				MemoryPurge();
				break;
			default:
				break;
		}
	}
}
