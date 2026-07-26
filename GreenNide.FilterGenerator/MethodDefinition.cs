using System.Collections.Generic;

namespace GreenNide.FilterGenerator;

internal sealed class MethodDefinition
{
    /// <summary>Имя метода: "HasItem"</summary>
    public string MethodName { get; set; } = "";

    /// <summary>
    ///     Свойства фильтра, которые замыкает метод.
    ///     Извлекаются из тела: filter.ItemId, filter.MinItemCount, ...
    /// </summary>
    public List<ClosureProperty> ClosureProperties { get; set; } = new();
}