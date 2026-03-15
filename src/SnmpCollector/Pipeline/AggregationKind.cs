namespace SnmpCollector.Pipeline;

/// <summary>
/// Specifies the arithmetic operation to apply when combining multiple metric values
/// into a single aggregate metric.
/// </summary>
public enum AggregationKind
{
    /// <summary>Sum all source values.</summary>
    Sum,
    /// <summary>Subtract sequentially: first minus second minus third, etc.</summary>
    Subtract,
    /// <summary>Absolute value of sequential subtraction.</summary>
    AbsDiff,
    /// <summary>Arithmetic mean of all source values.</summary>
    Mean
}
