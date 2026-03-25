namespace Blueprint.App.Backend;

/// <summary>
/// Database-agnostic canonical type used as intermediate representation
/// during cross-backend data transfer.
/// Maps 1-to-1 to a specific C# runtime type:
///   Text     → string
///   Boolean  → bool
///   Int32    → int
///   Int64    → long
///   Double   → double
///   Decimal  → decimal
///   Date     → DateOnly
///   DateTime → DateTime
///   Bytes    → byte[]
///   Guid     → Guid
///   Unknown  → pass-through (object?)
/// </summary>
public enum CanonicalType
{
    Text,
    Boolean,
    Int32,
    Int64,
    Double,
    Decimal,
    Date,
    DateTime,
    Bytes,
    Guid,
    Unknown
}
