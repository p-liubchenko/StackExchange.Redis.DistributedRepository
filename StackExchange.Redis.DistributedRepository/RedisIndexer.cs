using System.Linq.Expressions;
using System.Reflection;
using StackExchange.Redis.DistributedRepository.Indexing;

namespace StackExchange.Redis.DistributedRepository;

/// <summary>
/// Redis indexer for managing Redis keys.
/// </summary>
public class RedisIndexer<T> where T : class
{
	public Func<T, object> IndexSelector { get; set; }
	internal Expression<Func<T, object>> Index { get; set; }
	public string Name { get; private set; } = string.Empty;

	/// <summary>
	/// Initializes a new instance of the <see cref="RedisIndexer"/> class.
	/// </summary>
	/// <param name="database">The Redis database.</param>
	public RedisIndexer(string name, Expression<Func<T, object>> expression)
	{
		Name = name;
		Index = expression;
		IndexSelector = Index.Compile();
	}

	public static void ValidateIndexerExpression(Expression<Func<T, object>> expr)
	{
		var body = expr.Body is UnaryExpression unary && unary.NodeType == ExpressionType.Convert
			? unary.Operand
			: expr.Body;

		if (body is not MemberExpression memberExpr)
			throw new InvalidOperationException("Indexer must be a member access.");

		var memberType = ((PropertyInfo?)memberExpr.Member)?.PropertyType ?? memberExpr.Type;

		if (!IndexKeyHelper.IsIndexableType(memberType))
			throw new InvalidOperationException($"Cannot index member '{memberExpr.Member.Name}' of type '{memberType.Name}' — not supported.");
	}
}
