using System.Diagnostics;

namespace StackExchange.Redis.DistributedRepository.Telemetry;
public static class ActivitySourceProvider
{
	private static readonly Lazy<ActivitySource> _activitySource = new(() => new ActivitySource("StackExchange.Redis.DistributedRepository", "1.0.0"));
	public static ActivitySource GetActivitySource()
	{
		return _activitySource.Value;
	}

	public static Activity? StartActivity(string operationName, string? key = null)
	{
		Activity? activity = GetActivitySource().StartActivity(operationName, ActivityKind.Internal);
		if (activity is null)
			return null;
		activity.SetTag("operation.key", key);
		return activity;
	}
}
