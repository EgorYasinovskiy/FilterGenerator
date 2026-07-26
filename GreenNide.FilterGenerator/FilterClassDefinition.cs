using System.Collections.Generic;

namespace GreenNide.FilterGenerator
{
    internal sealed class FilterClassDefinition
    {
        /// <summary>Пространство имён класса фильтра</summary>
        public string Namespace { get; set; } = "";

        /// <summary>Имя исходного класса (например, "OrderFilterDefinition")</summary>
        public string ClassName { get; set; } = "";

        /// <summary>Имя сгенерированного класса со свойствами (например, "OrderFilterParams").
        /// Вычисляется по конвенции: {EntityName}FilterParams, или из атрибута ClassName.</summary>
        public string GeneratedClassName { get; set; } = "";

        /// <summary>Короткое имя сущности: "Order"</summary>
        public string EntityName { get; set; } = "";

        /// <summary>Полное имя сущности: "MyApp.Models.Order"</summary>
        public string EntityFullName { get; set; } = "";

        /// <summary>Поля-фильтры из Expression&lt;Func&lt;TEntity, TValue&gt;&gt;</summary>
        public List<FilterFieldDefinition> Fields { get; set; } = new();

        /// <summary>Методы-предикаты из static Expression&lt;Func&lt;TEntity, bool&gt;&gt;?(Filter)</summary>
        public List<MethodDefinition> Methods { get; set; } = new();
    }
}