
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
	/// Purges the repository
	/// </summary>
	/// <returns></returns>
	Task Purge();

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
