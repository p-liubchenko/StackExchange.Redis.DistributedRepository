using System.Globalization;

namespace StackExchange.Redis.DistributedRepository.Indexing;
internal class IndexKeyHelper
{
	private const string NULL = "null";
    public static string NormalizeValue(object? value)
    {
        return value switch
        {
            null => NULL,
            Enum e => Convert.ChangeType(e, Enum.GetUnderlyingType(e.GetType()), CultureInfo.InvariantCulture)?.ToString() ?? NULL,
            DateTime dt => dt.ToString("yyyyMMdd"),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("yyyy-MM-ddTHH:mm:sszzz"),
            bool b => b ? "1" : "0",
            Guid guid => guid.ToString("N"),
            int or
            short or
            long or
            byte or
            sbyte or
            uint or
            ushort or
            ulong => Convert.ToString(value, CultureInfo.InvariantCulture) ?? NULL,
            TimeSpan ts => ts.TotalMilliseconds.ToString(CultureInfo.InvariantCulture),
            string s => s,
            _ => value?.ToString() ?? NULL
		};
    }

	public static bool IsIndexableType(Type type)
	{
		type = Nullable.GetUnderlyingType(type) ?? type;

		return
			type == typeof(string) ||
			type == typeof(bool) ||
			type == typeof(Guid) ||
			type == typeof(DateTime) ||
			type == typeof(DateTimeOffset) ||
			type == typeof(TimeSpan) ||
			type.IsEnum ||
			type == typeof(int) ||
			type == typeof(long) ||
			type == typeof(short) ||
			type == typeof(byte) ||
			type == typeof(sbyte) ||
			type == typeof(uint) ||
			type == typeof(ulong) ||
			type == typeof(ushort);
	}

}
