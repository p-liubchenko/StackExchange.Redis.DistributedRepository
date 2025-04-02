using System.Text.Json;

namespace StackExchange.Redis.DistributedRepository.Extensions;
internal static class BinarySerializer
{
	public static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

	public static byte[] Serialize<T>(T item)
	{
		return JsonSerializer.SerializeToUtf8Bytes(item, _jsonOptions);
	}

	public static T? Deserialize<T>(byte[] data)
	{
		return JsonSerializer.Deserialize<T>(data, _jsonOptions);
	}
}
