using GreenNide.ExpressionFilter;

namespace GreenNide.FilterGenerator
{
    internal sealed class FilterFieldDefinition
    {
        /// <summary>Имя свойства фильтра: "CustomerName"</summary>
        public string PropertyName { get; set; } = "";

        /// <summary>C#-тип свойства: "string?", "decimal?", "int?"</summary>
        public string PropertyTypeCs { get; set; } = "";

        /// <summary>Путь в сущности: "Customer.Name", "Amount"</summary>
        public string EntityPath { get; set; } = "";

        /// <summary>Оператор сравнения</summary>
        public CompareOperator Operator { get; set; }

        /// <summary>Тип фильтра</summary>
        public FilterKind Kind { get; set; }

        /// <summary>
        /// Нужна ли null-защита для навигации.
        /// "Customer.Name" → "e.Customer != null"
        /// </summary>
        public string? NavigationNullGuard { get; set; }

        /// <summary>Исходная строка Expression (для FullText и подзапросов)</summary>
        public string? RawExpression { get; set; }

        /// <summary>True если тип — nullable value type (int?, decimal?, Guid? и т.д.).
        /// Используется для определения нужен ли .Value при генерации кода.</summary>
        public bool IsNullableValueType { get; set; }
    }
}