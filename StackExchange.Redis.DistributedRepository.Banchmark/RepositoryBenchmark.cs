using BenchmarkDotNet.Attributes;

namespace StackExchange.Redis.DistributedRepository.Banchmark;
public class RepositoryBenchmark
{
	MessageType MessageType = MessageType.Created;
	string testResourceKey = "testResourceKey";
	public RepositoryBenchmark()
	{
	}

	//[Benchmark]
	//public string GetMessageSB()
	//{
	//	//return DistributedHashRepository<object>.GenerateMessage(MessageType, testResourceKey);
	//}

	//[Benchmark]
	//public string GetMessageInterpolation()
	//{
	//	//return DistributedHashRepository<object>.GenerateMessageInterpolation(MessageType, testResourceKey);
	//}
}
