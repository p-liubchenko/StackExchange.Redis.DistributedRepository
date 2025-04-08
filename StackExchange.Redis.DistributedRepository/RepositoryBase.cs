namespace StackExchange.Redis.DistributedRepository;

public abstract class RepositoryBase<T> where T : class
{
	private string _baseKey;

	public virtual string BaseKey
	{
		get
		{
			_baseKey ??= $"dsr:{{{typeof(T).Name}}}";
			return _baseKey;
		}
	}
	public virtual string IndexBaseKey
	{
		get
		{
			return $"{BaseKey}:idx";
		}
	}
}
