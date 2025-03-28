using BenchmarkDotNet.Running;

namespace StackExchange.Redis.DistributedRepository.Banchmark;

internal class Program
{
	static void Main(string[] args)
	{
		// run the benchmark
		BenchmarkRunner.Run<RepositoryBenchmark>();
	}
}
