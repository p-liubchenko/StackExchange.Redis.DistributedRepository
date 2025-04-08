using System.Linq.Expressions;

namespace StackExchange.Redis.DistributedRepository;

/// <summary>
/// Redis indexer for managing Redis keys.
/// </summary>
public class RedisIndexer<T>
{
	public Func<T, object> IndexSelector { get; set; }
	internal Expression<Func<T, object>> Index { get; set; }
	public string Name { get; private set; } = string.Empty;

	/// <summary>
	/// Initializes a new instance of the <see cref="RedisIndexer"/> class.
	/// </summary>
	/// <param name="database">The Redis database.</param>
	public RedisIndexer(string name, Expression<Func<T, object>> expression)
	{
		Name = name;
		Index = expression;
		IndexSelector = Index.Compile();
	}
}
