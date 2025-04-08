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
			if (node.Left is MemberExpression memberExpr &&
				node.Right is ConstantExpression constExpr)
			{
				var name = memberExpr.Member.Name;

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
}
