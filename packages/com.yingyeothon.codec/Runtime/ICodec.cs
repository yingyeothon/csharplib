namespace Yingyeothon.Codec
{
    /// <summary>
    /// Converts values to and from a wire representation.
    /// </summary>
    /// <typeparam name="TWire">The wire representation, e.g. <see cref="string"/>.</typeparam>
    public interface ICodec<TWire>
    {
        /// <summary>Encodes a value into its wire representation.</summary>
        TWire Encode(JsonValue value);

        /// <summary>Decodes a wire representation back into a value.</summary>
        JsonValue Decode(TWire wire);
    }
}
