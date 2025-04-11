namespace StackExchange.Redis.DistributedRepository;
public interface IRepositoryMetrics
{
	void ObserveDuration(string operation, TimeSpan duration);
	void IncrementCounter(string operation);
}
