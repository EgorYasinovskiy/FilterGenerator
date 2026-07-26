using System;

namespace GreenNide.ExpressionFilter;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class GenerateFilterAttribute(Type entityType) : Attribute
{
    Type EntityType = entityType;

    /// <summary>
    ///     Имя генерируемого класса фильтра.
    ///     Если не задано, имя вычисляется автоматически по конвенции:
    ///     - {EntityName}FilterParams (Order → OrderFilterParams)
    /// </summary>
    public string? ClassName { get; set; }
}
