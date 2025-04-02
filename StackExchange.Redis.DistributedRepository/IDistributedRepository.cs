

namespace StackExchange.Redis.DistributedRepository;

/// <summary>
/// Interface for a distributed repository
/// </summary>
/// <typeparam name="T"></typeparam>
public interface IDistributedRepository<T> where T : class
{
	/// <summary>
	/// Adds an item to the repository
	/// </summary>
	/// <param name="item"></param>
	/// <returns></returns>
	T Add(T item);

	/// <summary>
	/// Adds an item to the repository
	/// </summary>
	/// <param name="item"></param>
	/// <returns></returns>
	Task<T> AddAsync(T item);
	void AddRange(IEnumerable<T> range);
	Task AddRangeAsync(IEnumerable<T> range);

	/// <summary>
	/// Gets all items from the repository
	/// </summary>
	/// <returns></returns>
	IEnumerable<T> Get();

	/// <summary>
	/// Gets an item from the repository by key
	/// </summary>
	/// <param name="Key"></param>
	/// <returns></returns>
	T? Get(string Key);

	/// <summary>
	/// Gets all items from the repository
	/// </summary>
	/// <returns></returns>
	Task<IEnumerable<T>> GetAsync();

	/// <summary>
	/// Gets an item from the repository by key
	/// </summary>
	/// <param name="Key"></param>
	/// <returns></returns>
	Task<T?> GetAsync(string Key);

	/// <summary>
	/// Gets an item from the repository by key, or adds it if it doesn't exist
	/// </summary>
	/// <param name="key"></param>
	/// <param name="factory"></param>
	/// <returns></returns>
	T GetOrAdd(string key, Func<T> factory);

	/// <summary>
	/// Gets an item from the repository by key, or adds it if it doesn't exist
	/// </summary>
	/// <param name="key"></param>
	/// <param name="factory"></param>
	/// <returns></returns>
	Task<T> GetOrAddAsync(string key, Func<Task<T>> factory);

	/// <summary>
	/// Purges the repository
	/// </summary>
	/// <returns></returns>
	Task Purge();
	Task RebakeAll();

	/// <summary>
	/// Rebuilds the repository, syncing with distributed cache with in-memory, taking distributed data container as source of truth
	/// </summary>
	/// <returns></returns>
	Task Rebuild();

	/// <summary>
	/// Removes an item from the repository by key
	/// </summary>
	/// <param name="key"></param>
	/// <returns></returns>
	T? Remove(string key);

	/// <summary>
	/// Removes an item from the repository
	/// </summary>
	/// <param name="item"></param>
	/// <returns></returns>
	T? Remove(T item);

	/// <summary>
	/// Removes an item from the repository by key
	/// </summary>
	/// <param name="key"></param>
	/// <returns></returns>
	Task<T?> RemoveAsync(string key);

	/// <summary>
	/// Removes an item from the repository
	/// </summary>
	/// <param name="item"></param>
	/// <returns></returns>
	Task<T?> RemoveAsync(T item);
}
