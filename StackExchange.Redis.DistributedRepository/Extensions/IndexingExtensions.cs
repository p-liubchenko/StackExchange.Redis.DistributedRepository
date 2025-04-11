using System.Linq.Expressions;

namespace StackExchange.Redis.DistributedRepository.Extensions;
internal static class IndexingExtensions
{
	public static string ExtractPropertyName<T>(Expression<Func<T, object>> expression)
	{
		Expression body = expression.Body;

		if (body is UnaryExpression unary && unary.NodeType == ExpressionType.Convert)
			body = unary.Operand;

		if (body is MemberExpression member)
			return member.Member.Name;

		if (body is MemberExpression nested)
			return GetFullPropertyPath(nested);

		throw new InvalidOperationException($"Unsupported indexer expression: {expression}");
	}
	private static string GetFullPropertyPath(MemberExpression expression)
	{
		var parts = new Stack<string>();
		Expression? current = expression;

		while (current is MemberExpression member)
		{
			parts.Push(member.Member.Name);
			current = member.Expression;
		}

		return string.Join(".", parts);
	}

}
