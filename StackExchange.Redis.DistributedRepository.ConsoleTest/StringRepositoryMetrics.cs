using System.Text;

namespace StackExchange.Redis.DistributedRepository.ConsoleTest;
public class StringRepositoryMetrics : IRepositoryMetrics
{
	public static Dictionary<string, long> observations = new();
	public static Dictionary<string, int> counters = new();

	public void IncrementCounter(string operation)
	{
		if (!counters.ContainsKey(operation))
		{
			counters.Add(operation, 0);
		}
		else
		{
			counters[operation] = counters[operation]++;
		}
	}

	public void ObserveDuration(string operation, TimeSpan duration)
	{
		if (observations.ContainsKey(operation))
		{
			observations[operation] += duration.Ticks;
		}
		else
		{
			observations.Add(operation, duration.Ticks);
		}
	}

	public static string ToUserFriendlyString()
	{
		var sb = new StringBuilder();
		sb.AppendLine("Observations:");
		foreach (var observation in observations)
		{
			sb.AppendLine($"{observation.Key}: {TimeSpan.FromTicks(observation.Value)}");
		}
		sb.AppendLine("Counters:");
		foreach (var counter in counters)
		{
			sb.AppendLine($"{counter.Key}: {counter.Value}");
		}
		return sb.ToString();	
	}
}
