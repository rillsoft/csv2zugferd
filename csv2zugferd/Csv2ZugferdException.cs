namespace Csv2Zugferd;

/// <summary>
/// Fachliche Ablehnung mit stabilem englischem Code (<see cref="ErrorCodes"/>) und deutschem Text.
/// Tests vergleichen nur den Code, nie den Text.
/// </summary>
public class Csv2ZugferdException : Exception
{
    public Csv2ZugferdException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public Csv2ZugferdException(string code, string message, Exception? innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    /// <summary>Code aus <see cref="ErrorCodes"/>.</summary>
    public string Code { get; }
}
