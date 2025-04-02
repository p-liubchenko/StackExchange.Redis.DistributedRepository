using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StackExchange.Redis.DistributedRepository;
public abstract class RepositoryBase<T> where T : class
{
	private string _baseKey;

	public string BaseKey
	{
		get
		{
			_baseKey ??= $"dsr:{typeof(T).Name}";
			return _baseKey;
		}
	}
}
