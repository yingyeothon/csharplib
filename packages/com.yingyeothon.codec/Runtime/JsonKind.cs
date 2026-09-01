namespace Yingyeothon.Codec
{
    /// <summary>The six JSON value kinds.</summary>
    public enum JsonKind
    {
        /// <summary>JSON <c>null</c>. Distinct from a field that was absent.</summary>
        Null,
        /// <summary><c>true</c> or <c>false</c>.</summary>
        Bool,
        /// <summary>A JSON number, held as a <c>double</c>.</summary>
        Number,
        /// <summary>A JSON string.</summary>
        String,
        /// <summary>A JSON array.</summary>
        Array,
        /// <summary>A JSON object.</summary>
        Object,
    }
}
