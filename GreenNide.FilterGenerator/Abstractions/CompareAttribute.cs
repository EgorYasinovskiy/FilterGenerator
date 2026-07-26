using System;

namespace GreenNide.ExpressionFilter;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class CompareAttribute : Attribute
{
    public CompareAttribute(CompareOperator op)
    {
        Operator = op;
    }

    public CompareOperator Operator { get; }
}
