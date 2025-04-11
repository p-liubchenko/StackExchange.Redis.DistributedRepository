using System.Linq.Expressions;

namespace StackExchange.Redis.DistributedRepository.Indexing;
class IndexConditionExtractor<T> : ExpressionVisitor
{
	private readonly Dictionary<string, Expression<Func<T, object>>> _indexers;
	public List<IndexedCondition> IndexedMatches { get; } = new();

	public IndexConditionExtractor(Dictionary<string, Expression<Func<T, object>>> indexers)
	{
		_indexers = indexers;
	}

	protected override Expression VisitBinary(BinaryExpression node)
	{
		if (node.NodeType == ExpressionType.Equal)
		{
			MemberExpression? leftMember = UnwrapToMember(node.Left);
			MemberExpression? rightMember = UnwrapToMember(node.Right);

			ConstantExpression? constExpr = null;
			MemberExpression? memberExpr = null;

			if (leftMember != null && node.Right is ConstantExpression rightConst)
			{
				memberExpr = leftMember;
				constExpr = rightConst;
			}
			else if (rightMember != null && node.Left is ConstantExpression leftConst)
			{
				memberExpr = rightMember;
				constExpr = leftConst;
			}

			if (memberExpr != null && constExpr != null)
			{
				string? name = memberExpr.Member.Name;

				if (_indexers.ContainsKey(name))
				{
					IndexedMatches.Add(new IndexedCondition
					{
						IndexName = name,
						Value = constExpr.Value!
					});
				}
			}
		}

		return base.VisitBinary(node);
	}

	private static MemberExpression? UnwrapToMember(Expression expression)
	{
		return expression switch
		{
			MemberExpression m => m,
			UnaryExpression u when u.Operand is MemberExpression m => m,
			_ => null
		};
	}
}
