namespace StackExchange.Redis.DistributedRepository.Extensions;
internal static class RepositoryExtensions
{

	/// <summary>
	/// Returns fully qualified key
	/// </summary>
	/// <param name="key"></param>
	/// <returns></returns>
	public static string FQK<T>(this DistributedRepository<T> repo, string key) where T : class => 
		$"{repo.BaseKey}:{key}";

	/// <summary>
	/// Returns fully qualified key
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="repo"></param>
	/// <param name="key"></param>
	/// <returns></returns>
	public static string FQK<T>(this DistributedBackedRepository<T> repo, string key) where T : class =>

		$"{repo.BaseKey}:{key}";
	/// <summary>
	/// Returns fully qualified key
	/// </summary>
	/// <param name="item"></param>
	/// <returns></returns>
	private static string FQK<T>(this DistributedRepository<T> repo, T item) where T : class => 
		$"{repo.BaseKey}:{repo.KeySelector.Invoke(item)}";
}
