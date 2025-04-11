namespace StackExchange.Redis.DistributedRepository;

public abstract class RepositoryBase<T> where T : class
{
	protected string? _globalPrefix;
	
	private string _baseKey;
	public virtual string BaseKey
	{
		get
		{
			_baseKey ??= $"{_globalPrefix}dsr:{{{typeof(T).Name}}}";
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
