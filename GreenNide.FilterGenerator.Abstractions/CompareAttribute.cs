using System;

namespace GreenNide.ExpressionFilter;

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class CompareAttribute : Attribute
    {
        public CompareOperator Operator { get; }
        public CompareAttribute(CompareOperator op) => Operator = op;
    }
    