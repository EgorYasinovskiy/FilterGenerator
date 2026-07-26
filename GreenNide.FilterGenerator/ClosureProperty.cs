namespace GreenNide.FilterGenerator;

internal sealed class ClosureProperty
{
    /// <summary>Имя свойства фильтра: "ItemId"</summary>
    public string PropertyName { get; set; } = "";

    /// <summary>C#-тип: "int?", "decimal?"</summary>
    public string PropertyTypeCs { get; set; } = "";
}