using System.Globalization;

namespace Blueprint.App.Backend;

/// <summary>
/// Cross-backend type mapping table — the single source of truth for type equivalences.
///
/// Three mapping groups per backend:
///   A. SQL type name  → CanonicalType  (used when reading source column schema)
///   B. CanonicalType  → DDL type string (used when generating CREATE TABLE / ADD COLUMN)
///
/// Canonical C# value types (used in transfer pipeline):
///   CanonicalType.Text     ↔  string
///   CanonicalType.Boolean  ↔  bool
///   CanonicalType.Int32    ↔  int
///   CanonicalType.Int64    ↔  long
///   CanonicalType.Double   ↔  double
///   CanonicalType.Decimal  ↔  decimal
///   CanonicalType.Date     ↔  DateOnly
///   CanonicalType.DateTime ↔  DateTime
///   CanonicalType.Bytes    ↔  byte[]
///   CanonicalType.Guid     ↔  Guid
///   CanonicalType.Unknown  ↔  object? (pass-through)
///
/// ── Cross-backend equivalence table ─────────────────────────────────────────
/// Canonical   SQLite      PostgreSQL              MySQL           SQL Server      Access          Firebird        DB2             Oracle          DBase
/// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
/// Text        TEXT        text/varchar/char       varchar/text    nvarchar/varchar TEXT            VARCHAR/CHAR    VARCHAR/CLOB    VARCHAR2/CLOB   CHARACTER
/// Boolean     INTEGER     boolean                 tinyint(1)      bit             YESNO           BOOLEAN         SMALLINT        NUMBER(1)       LOGICAL
/// Int32       INTEGER     integer/smallint        int/smallint    int/smallint    INTEGER         INTEGER/SMALLINT INTEGER         NUMBER(10,0)    NUMERIC
/// Int64       INTEGER     bigint                  bigint          bigint          LONG*           BIGINT          BIGINT          NUMBER(19,0)    NUMERIC
/// Double      REAL        real/double precision   float/double    float/real      DOUBLE          FLOAT/DOUBLE    DOUBLE          BINARY_DOUBLE   FLOAT
/// Decimal     NUMERIC     numeric/decimal         decimal/numeric decimal/numeric DECIMAL         DECIMAL/NUMERIC DECIMAL         NUMBER          NUMERIC
/// Date        TEXT        date                    date            date            DATETIME**      DATE            DATE            DATE***         DATE
/// DateTime    TEXT        timestamp               datetime        datetime2       DATETIME        TIMESTAMP       TIMESTAMP       TIMESTAMP       DATE
/// Bytes       BLOB        bytea                   blob/varbinary  varbinary(max)  LONGBINARY      BLOB            BLOB            BLOB/RAW        -
/// Guid        TEXT        uuid                    char(36)        uniqueidentifier GUID           CHAR(36)        CHAR(36)        RAW(16)         -
///
/// * Access LONG = 32-bit; no native 64-bit. Values clamped to Int32 range.
/// ** Access DATE type stores both date and time.
/// *** Oracle DATE includes time component; use TRUNC() to get date-only.
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
public static class TypeMappings
{
    // ── A. SQL type name → CanonicalType ─────────────────────────────────────
    // Key: upper-case base type name (strip length/precision, e.g. "VARCHAR(255)" → "VARCHAR")
    // Used by each connector's GetColumnTypes() implementation.

    private static readonly IReadOnlyDictionary<BackendType, IReadOnlyDictionary<string, CanonicalType>> ToCanonical =
        new Dictionary<BackendType, IReadOnlyDictionary<string, CanonicalType>>
    {
        [BackendType.SQLite] = new Dictionary<string, CanonicalType>(StringComparer.OrdinalIgnoreCase)
        {
            // Text affinity
            ["TEXT"]              = CanonicalType.Text,
            ["VARCHAR"]           = CanonicalType.Text,
            ["NVARCHAR"]          = CanonicalType.Text,
            ["CHAR"]              = CanonicalType.Text,
            ["NCHAR"]             = CanonicalType.Text,
            ["CHARACTER"]         = CanonicalType.Text,
            ["CHARACTER VARYING"] = CanonicalType.Text,
            ["CLOB"]              = CanonicalType.Text,
            ["TINYTEXT"]          = CanonicalType.Text,
            ["MEDIUMTEXT"]        = CanonicalType.Text,
            ["LONGTEXT"]          = CanonicalType.Text,
            ["STRING"]            = CanonicalType.Text,
            // Boolean
            ["BOOLEAN"]           = CanonicalType.Boolean,
            ["BOOL"]              = CanonicalType.Boolean,
            ["BIT"]               = CanonicalType.Boolean,
            // Integer affinity (SQLite uses 64-bit storage for all integers)
            ["INTEGER"]           = CanonicalType.Int64,
            ["INT"]               = CanonicalType.Int64,
            ["INT2"]              = CanonicalType.Int64,
            ["INT4"]              = CanonicalType.Int64,
            ["INT8"]              = CanonicalType.Int64,
            ["TINYINT"]           = CanonicalType.Int64,
            ["SMALLINT"]          = CanonicalType.Int64,
            ["MEDIUMINT"]         = CanonicalType.Int64,
            ["BIGINT"]            = CanonicalType.Int64,
            ["UNSIGNED BIG INT"]  = CanonicalType.Int64,
            // Real affinity
            ["REAL"]              = CanonicalType.Double,
            ["FLOAT"]             = CanonicalType.Double,
            ["FLOAT4"]            = CanonicalType.Double,
            ["FLOAT8"]            = CanonicalType.Double,
            ["DOUBLE"]            = CanonicalType.Double,
            ["DOUBLE PRECISION"]  = CanonicalType.Double,
            // Numeric affinity
            ["NUMERIC"]           = CanonicalType.Decimal,
            ["DECIMAL"]           = CanonicalType.Decimal,
            ["NUMBER"]            = CanonicalType.Decimal,
            // Date/time (SQLite stores as TEXT)
            ["DATE"]              = CanonicalType.Date,
            ["DATETIME"]          = CanonicalType.DateTime,
            ["TIMESTAMP"]         = CanonicalType.DateTime,
            // Blob affinity
            ["BLOB"]              = CanonicalType.Bytes,
            ["BINARY"]            = CanonicalType.Bytes,
            ["VARBINARY"]         = CanonicalType.Bytes,
            // Guid
            ["UUID"]              = CanonicalType.Guid,
            ["UNIQUEIDENTIFIER"]  = CanonicalType.Guid,
        },

        [BackendType.PostgreSQL] = new Dictionary<string, CanonicalType>(StringComparer.OrdinalIgnoreCase)
        {
            // Text (information_schema data_type values)
            ["text"]                        = CanonicalType.Text,
            ["character varying"]           = CanonicalType.Text,
            ["character"]                   = CanonicalType.Text,
            ["name"]                        = CanonicalType.Text,
            ["bpchar"]                      = CanonicalType.Text,
            ["varchar"]                     = CanonicalType.Text,
            ["json"]                        = CanonicalType.Text,
            ["jsonb"]                       = CanonicalType.Text,
            ["xml"]                         = CanonicalType.Text,
            // Boolean
            ["boolean"]                     = CanonicalType.Boolean,
            // Integer
            ["smallint"]                    = CanonicalType.Int32,
            ["integer"]                     = CanonicalType.Int32,
            ["bigint"]                      = CanonicalType.Int64,
            ["int2"]                        = CanonicalType.Int32,
            ["int4"]                        = CanonicalType.Int32,
            ["int8"]                        = CanonicalType.Int64,
            // Floating point
            ["real"]                        = CanonicalType.Double,
            ["double precision"]            = CanonicalType.Double,
            ["float4"]                      = CanonicalType.Double,
            ["float8"]                      = CanonicalType.Double,
            // Decimal
            ["numeric"]                     = CanonicalType.Decimal,
            ["decimal"]                     = CanonicalType.Decimal,
            ["money"]                       = CanonicalType.Decimal,
            // Date/time
            ["date"]                        = CanonicalType.Date,
            ["timestamp without time zone"] = CanonicalType.DateTime,
            ["timestamp with time zone"]    = CanonicalType.DateTime,
            ["timestamp"]                   = CanonicalType.DateTime,
            // Bytes
            ["bytea"]                       = CanonicalType.Bytes,
            // Guid
            ["uuid"]                        = CanonicalType.Guid,
        },

        [BackendType.MySQL] = new Dictionary<string, CanonicalType>(StringComparer.OrdinalIgnoreCase)
        {
            // Text
            ["varchar"]     = CanonicalType.Text,
            ["nvarchar"]    = CanonicalType.Text,
            ["char"]        = CanonicalType.Text,
            ["nchar"]       = CanonicalType.Text,
            ["text"]        = CanonicalType.Text,
            ["tinytext"]    = CanonicalType.Text,
            ["mediumtext"]  = CanonicalType.Text,
            ["longtext"]    = CanonicalType.Text,
            ["enum"]        = CanonicalType.Text,
            ["set"]         = CanonicalType.Text,
            ["json"]        = CanonicalType.Text,
            // Boolean (MySQL uses tinyint(1) for bool)
            ["bool"]        = CanonicalType.Boolean,
            ["boolean"]     = CanonicalType.Boolean,
            // Integer
            ["tinyint"]     = CanonicalType.Int32,
            ["smallint"]    = CanonicalType.Int32,
            ["mediumint"]   = CanonicalType.Int32,
            ["int"]         = CanonicalType.Int32,
            ["integer"]     = CanonicalType.Int32,
            ["bigint"]      = CanonicalType.Int64,
            // Float
            ["float"]       = CanonicalType.Double,
            ["double"]      = CanonicalType.Double,
            ["real"]        = CanonicalType.Double,
            // Decimal
            ["decimal"]     = CanonicalType.Decimal,
            ["numeric"]     = CanonicalType.Decimal,
            // Date/time
            ["date"]        = CanonicalType.Date,
            ["datetime"]    = CanonicalType.DateTime,
            ["timestamp"]   = CanonicalType.DateTime,
            ["time"]        = CanonicalType.Text,
            ["year"]        = CanonicalType.Int32,
            // Bytes
            ["binary"]      = CanonicalType.Bytes,
            ["varbinary"]   = CanonicalType.Bytes,
            ["blob"]        = CanonicalType.Bytes,
            ["tinyblob"]    = CanonicalType.Bytes,
            ["mediumblob"]  = CanonicalType.Bytes,
            ["longblob"]    = CanonicalType.Bytes,
        },

        [BackendType.SqlServer] = new Dictionary<string, CanonicalType>(StringComparer.OrdinalIgnoreCase)
        {
            // Text
            ["varchar"]           = CanonicalType.Text,
            ["nvarchar"]          = CanonicalType.Text,
            ["char"]              = CanonicalType.Text,
            ["nchar"]             = CanonicalType.Text,
            ["text"]              = CanonicalType.Text,
            ["ntext"]             = CanonicalType.Text,
            ["xml"]               = CanonicalType.Text,
            ["sysname"]           = CanonicalType.Text,
            // Boolean
            ["bit"]               = CanonicalType.Boolean,
            // Integer
            ["tinyint"]           = CanonicalType.Int32,
            ["smallint"]          = CanonicalType.Int32,
            ["int"]               = CanonicalType.Int32,
            ["bigint"]            = CanonicalType.Int64,
            // Float
            ["real"]              = CanonicalType.Double,
            ["float"]             = CanonicalType.Double,
            // Decimal
            ["decimal"]           = CanonicalType.Decimal,
            ["numeric"]           = CanonicalType.Decimal,
            ["money"]             = CanonicalType.Decimal,
            ["smallmoney"]        = CanonicalType.Decimal,
            // Date/time
            ["date"]              = CanonicalType.Date,
            ["datetime"]          = CanonicalType.DateTime,
            ["datetime2"]         = CanonicalType.DateTime,
            ["smalldatetime"]     = CanonicalType.DateTime,
            ["datetimeoffset"]    = CanonicalType.DateTime,
            // Bytes
            ["binary"]            = CanonicalType.Bytes,
            ["varbinary"]         = CanonicalType.Bytes,
            ["image"]             = CanonicalType.Bytes,
            ["timestamp"]         = CanonicalType.Bytes,
            ["rowversion"]        = CanonicalType.Bytes,
            // Guid
            ["uniqueidentifier"]  = CanonicalType.Guid,
        },

        [BackendType.Access] = new Dictionary<string, CanonicalType>(StringComparer.OrdinalIgnoreCase)
        {
            // Text
            ["TEXT"]        = CanonicalType.Text,
            ["VARCHAR"]     = CanonicalType.Text,
            ["NVARCHAR"]    = CanonicalType.Text,
            ["CHAR"]        = CanonicalType.Text,
            ["LONGTEXT"]    = CanonicalType.Text,
            ["MEMO"]        = CanonicalType.Text,
            ["LONGCHAR"]    = CanonicalType.Text,
            // Boolean (Access Yes/No)
            ["YESNO"]       = CanonicalType.Boolean,
            ["BOOLEAN"]     = CanonicalType.Boolean,
            ["BIT"]         = CanonicalType.Boolean,
            // Integer (Access LONG = 32-bit!)
            ["INTEGER"]     = CanonicalType.Int32,
            ["INT"]         = CanonicalType.Int32,
            ["SMALLINT"]    = CanonicalType.Int32,
            ["TINYINT"]     = CanonicalType.Int32,
            ["LONG"]        = CanonicalType.Int32,   // Access LONG = 32-bit Long Integer
            ["SHORT"]       = CanonicalType.Int32,
            ["BYTE"]        = CanonicalType.Int32,
            // Float
            ["SINGLE"]      = CanonicalType.Double,
            ["DOUBLE"]      = CanonicalType.Double,
            ["FLOAT"]       = CanonicalType.Double,
            // Decimal
            ["DECIMAL"]     = CanonicalType.Decimal,
            ["NUMERIC"]     = CanonicalType.Decimal,
            ["CURRENCY"]    = CanonicalType.Decimal,
            ["MONEY"]       = CanonicalType.Decimal,
            // Date/time (Access has no separate DATE; all is DATETIME)
            ["DATETIME"]    = CanonicalType.DateTime,
            ["DATE"]        = CanonicalType.DateTime,
            // Bytes
            ["BINARY"]      = CanonicalType.Bytes,
            ["VARBINARY"]   = CanonicalType.Bytes,
            ["LONGBINARY"]  = CanonicalType.Bytes,
            ["IMAGE"]       = CanonicalType.Bytes,
            // Guid
            ["GUID"]        = CanonicalType.Guid,
            ["UNIQUEIDENTIFIER"] = CanonicalType.Guid,
            // ADO DataTypeEnum konstantna imena (iz originalne VBA Blueprint aplikacije)
            ["adBoolean"]        = CanonicalType.Boolean,
            ["adTinyInt"]        = CanonicalType.Int32,
            ["adSmallInt"]       = CanonicalType.Int32,
            ["adInteger"]        = CanonicalType.Int32,
            ["adLong"]           = CanonicalType.Int32,   // VBA Long Integer = 32-bit
            ["adUnsignedTinyInt"]  = CanonicalType.Int32,
            ["adUnsignedSmallInt"] = CanonicalType.Int32,
            ["adUnsignedInt"]      = CanonicalType.Int32,
            ["adBigInt"]           = CanonicalType.Int64,
            ["adUnsignedBigInt"]   = CanonicalType.Int64,
            ["adSingle"]         = CanonicalType.Double,
            ["adDouble"]         = CanonicalType.Double,
            ["adCurrency"]       = CanonicalType.Decimal,
            ["adDecimal"]        = CanonicalType.Decimal,
            ["adNumeric"]        = CanonicalType.Decimal,
            ["adDate"]           = CanonicalType.DateTime,
            ["adDBDate"]         = CanonicalType.Date,
            ["adDBTime"]         = CanonicalType.DateTime,
            ["adDBTimeStamp"]    = CanonicalType.DateTime,
            ["adChar"]           = CanonicalType.Text,
            ["adVarChar"]        = CanonicalType.Text,
            ["adLongVarChar"]    = CanonicalType.Text,
            ["adWChar"]          = CanonicalType.Text,
            ["adVarWChar"]       = CanonicalType.Text,
            ["adLongVarWChar"]   = CanonicalType.Text,
            ["adBSTR"]           = CanonicalType.Text,
            ["adBinary"]         = CanonicalType.Bytes,
            ["adVarBinary"]      = CanonicalType.Bytes,
            ["adLongVarBinary"]  = CanonicalType.Bytes,
            ["adGUID"]           = CanonicalType.Guid,
            // Matrix-specific ADO aliases not in standard ADO DataTypeEnum
            ["adMemo"]           = CanonicalType.Text,    // Memo / Long Text field
            ["adOLE"]            = CanonicalType.Bytes,   // OLE Object → binary blob
            ["adVariant"]        = CanonicalType.Unknown, // Variant → pass-through
            ["AutoNumber"]       = CanonicalType.Int32,   // Auto-increment PK (VBA AutoNumber)
        },

        [BackendType.Firebird] = new Dictionary<string, CanonicalType>(StringComparer.OrdinalIgnoreCase)
        {
            // Text
            ["VARCHAR"]           = CanonicalType.Text,
            ["CHAR"]              = CanonicalType.Text,
            ["CHARACTER"]         = CanonicalType.Text,
            ["CHARACTER VARYING"] = CanonicalType.Text,
            ["VARYING"]           = CanonicalType.Text,
            ["BLOB SUB_TYPE 1"]   = CanonicalType.Text,   // BLOB TEXT subtype
            ["BLOB SUB_TYPE TEXT"]= CanonicalType.Text,
            // Boolean (Firebird 3.0+)
            ["BOOLEAN"]           = CanonicalType.Boolean,
            // Integer
            ["SMALLINT"]          = CanonicalType.Int32,
            ["INTEGER"]           = CanonicalType.Int32,
            ["INT"]               = CanonicalType.Int32,
            ["BIGINT"]            = CanonicalType.Int64,
            // Float
            ["FLOAT"]             = CanonicalType.Double,
            ["DOUBLE PRECISION"]  = CanonicalType.Double,
            // Decimal
            ["DECIMAL"]           = CanonicalType.Decimal,
            ["NUMERIC"]           = CanonicalType.Decimal,
            // Date/time
            ["DATE"]              = CanonicalType.Date,
            ["TIMESTAMP"]         = CanonicalType.DateTime,
            ["TIME"]              = CanonicalType.Text,    // No canonical time type
            // Bytes
            ["BLOB"]              = CanonicalType.Bytes,
            ["BLOB SUB_TYPE 0"]   = CanonicalType.Bytes,
            // Guid (stored as CHAR(36))
            ["CHAR(36)"]          = CanonicalType.Guid,
        },

        [BackendType.DB2] = new Dictionary<string, CanonicalType>(StringComparer.OrdinalIgnoreCase)
        {
            // Text
            ["VARCHAR"]       = CanonicalType.Text,
            ["CHAR"]          = CanonicalType.Text,
            ["CHARACTER"]     = CanonicalType.Text,
            ["CLOB"]          = CanonicalType.Text,
            ["LONG VARCHAR"]  = CanonicalType.Text,
            ["GRAPHIC"]       = CanonicalType.Text,
            ["VARGRAPHIC"]    = CanonicalType.Text,
            ["DBCLOB"]        = CanonicalType.Text,
            ["XML"]           = CanonicalType.Text,
            // Boolean (DB2 has no native BOOLEAN; uses SMALLINT)
            ["BOOLEAN"]       = CanonicalType.Boolean,
            // Integer
            ["SMALLINT"]      = CanonicalType.Int32,
            ["INTEGER"]       = CanonicalType.Int32,
            ["INT"]           = CanonicalType.Int32,
            ["BIGINT"]        = CanonicalType.Int64,
            // Float
            ["REAL"]          = CanonicalType.Double,
            ["FLOAT"]         = CanonicalType.Double,
            ["DOUBLE"]        = CanonicalType.Double,
            ["DOUBLE PRECISION"] = CanonicalType.Double,
            ["DECFLOAT"]      = CanonicalType.Double,
            // Decimal
            ["DECIMAL"]       = CanonicalType.Decimal,
            ["NUMERIC"]       = CanonicalType.Decimal,
            ["DEC"]           = CanonicalType.Decimal,
            ["NUM"]           = CanonicalType.Decimal,
            // Date/time
            ["DATE"]          = CanonicalType.Date,
            ["TIMESTAMP"]     = CanonicalType.DateTime,
            ["TIME"]          = CanonicalType.Text,
            // Bytes
            ["BLOB"]          = CanonicalType.Bytes,
            ["BINARY"]        = CanonicalType.Bytes,
            ["VARBINARY"]     = CanonicalType.Bytes,
        },

        [BackendType.Oracle] = new Dictionary<string, CanonicalType>(StringComparer.OrdinalIgnoreCase)
        {
            // Text
            ["VARCHAR2"]      = CanonicalType.Text,
            ["NVARCHAR2"]     = CanonicalType.Text,
            ["CHAR"]          = CanonicalType.Text,
            ["NCHAR"]         = CanonicalType.Text,
            ["CLOB"]          = CanonicalType.Text,
            ["NCLOB"]         = CanonicalType.Text,
            ["LONG"]          = CanonicalType.Text,
            ["XMLTYPE"]       = CanonicalType.Text,
            // Boolean (Oracle SQL has no BOOLEAN; NUMBER(1) used as convention)
            ["NUMBER(1)"]     = CanonicalType.Boolean,
            // Integer (Oracle NUMBER(p,0))
            ["NUMBER(10,0)"]  = CanonicalType.Int32,
            ["NUMBER(19,0)"]  = CanonicalType.Int64,
            ["INTEGER"]       = CanonicalType.Int64,
            ["INT"]           = CanonicalType.Int64,
            ["SMALLINT"]      = CanonicalType.Int32,
            // Float / Decimal (Oracle NUMBER without scale = flexible)
            ["NUMBER"]        = CanonicalType.Decimal,
            ["DECIMAL"]       = CanonicalType.Decimal,
            ["NUMERIC"]       = CanonicalType.Decimal,
            ["FLOAT"]         = CanonicalType.Double,
            ["BINARY_FLOAT"]  = CanonicalType.Double,
            ["BINARY_DOUBLE"]  = CanonicalType.Double,
            // Date/time (Oracle DATE includes time component!)
            ["DATE"]          = CanonicalType.DateTime,
            ["TIMESTAMP"]     = CanonicalType.DateTime,
            ["TIMESTAMP WITH TIME ZONE"]      = CanonicalType.DateTime,
            ["TIMESTAMP WITH LOCAL TIME ZONE"]= CanonicalType.DateTime,
            // Bytes
            ["BLOB"]          = CanonicalType.Bytes,
            ["RAW"]           = CanonicalType.Bytes,
            ["LONG RAW"]      = CanonicalType.Bytes,
            // Guid (Oracle stores as RAW(16) or CHAR(36))
            ["RAW(16)"]       = CanonicalType.Guid,
        },

        [BackendType.DBase] = new Dictionary<string, CanonicalType>(StringComparer.OrdinalIgnoreCase)
        {
            // Text
            ["CHARACTER"]   = CanonicalType.Text,
            ["CHAR"]        = CanonicalType.Text,
            ["VARCHAR"]     = CanonicalType.Text,
            ["MEMO"]        = CanonicalType.Text,
            // Boolean (DBase LOGICAL: T/F/.T./.F.)
            ["LOGICAL"]     = CanonicalType.Boolean,
            ["BOOLEAN"]     = CanonicalType.Boolean,
            // Integer
            ["INTEGER"]     = CanonicalType.Int32,
            ["INT"]         = CanonicalType.Int32,
            ["SMALLINT"]    = CanonicalType.Int32,
            ["AUTOINC"]     = CanonicalType.Int32,
            // Float
            ["FLOAT"]       = CanonicalType.Double,
            ["DOUBLE"]      = CanonicalType.Double,
            // Decimal
            ["NUMERIC"]     = CanonicalType.Decimal,
            ["DECIMAL"]     = CanonicalType.Decimal,
            ["MONEY"]       = CanonicalType.Decimal,
            ["CURRENCY"]    = CanonicalType.Decimal,
            // Date/time
            ["DATE"]        = CanonicalType.Date,
            ["DATETIME"]    = CanonicalType.DateTime,
            ["TIMESTAMP"]   = CanonicalType.DateTime,
        },
    };

    // ── B. CanonicalType → DDL type string per backend ────────────────────────
    // Used by each connector's CreateTable / AddColumn when generating DDL.
    // MaxLength is appended separately when > 0.

    private static readonly IReadOnlyDictionary<BackendType, IReadOnlyDictionary<CanonicalType, string>> ToDdl =
        new Dictionary<BackendType, IReadOnlyDictionary<CanonicalType, string>>
    {
        [BackendType.SQLite] = new Dictionary<CanonicalType, string>
        {
            [CanonicalType.Text]     = "TEXT",
            [CanonicalType.Boolean]  = "INTEGER",     // SQLite has no native BOOLEAN
            [CanonicalType.Int32]    = "INTEGER",
            [CanonicalType.Int64]    = "INTEGER",
            [CanonicalType.Double]   = "REAL",
            [CanonicalType.Decimal]  = "NUMERIC",
            [CanonicalType.Date]     = "TEXT",         // SQLite stores dates as ISO text
            [CanonicalType.DateTime] = "TEXT",
            [CanonicalType.Bytes]    = "BLOB",
            [CanonicalType.Guid]     = "TEXT",
            [CanonicalType.Unknown]  = "TEXT",
        },
        [BackendType.PostgreSQL] = new Dictionary<CanonicalType, string>
        {
            [CanonicalType.Text]     = "TEXT",
            [CanonicalType.Boolean]  = "BOOLEAN",
            [CanonicalType.Int32]    = "INTEGER",
            [CanonicalType.Int64]    = "BIGINT",
            [CanonicalType.Double]   = "DOUBLE PRECISION",
            [CanonicalType.Decimal]  = "NUMERIC",
            [CanonicalType.Date]     = "DATE",
            [CanonicalType.DateTime] = "TIMESTAMP",
            [CanonicalType.Bytes]    = "BYTEA",
            [CanonicalType.Guid]     = "UUID",
            [CanonicalType.Unknown]  = "TEXT",
        },
        [BackendType.MySQL] = new Dictionary<CanonicalType, string>
        {
            [CanonicalType.Text]     = "LONGTEXT",
            [CanonicalType.Boolean]  = "TINYINT(1)",
            [CanonicalType.Int32]    = "INT",
            [CanonicalType.Int64]    = "BIGINT",
            [CanonicalType.Double]   = "DOUBLE",
            [CanonicalType.Decimal]  = "DECIMAL(18,4)",
            [CanonicalType.Date]     = "DATE",
            [CanonicalType.DateTime] = "DATETIME",
            [CanonicalType.Bytes]    = "LONGBLOB",
            [CanonicalType.Guid]     = "CHAR(36)",
            [CanonicalType.Unknown]  = "LONGTEXT",
        },
        [BackendType.SqlServer] = new Dictionary<CanonicalType, string>
        {
            [CanonicalType.Text]     = "NVARCHAR(MAX)",
            [CanonicalType.Boolean]  = "BIT",
            [CanonicalType.Int32]    = "INT",
            [CanonicalType.Int64]    = "BIGINT",
            [CanonicalType.Double]   = "FLOAT",
            [CanonicalType.Decimal]  = "DECIMAL(18,4)",
            [CanonicalType.Date]     = "DATE",
            [CanonicalType.DateTime] = "DATETIME2",
            [CanonicalType.Bytes]    = "VARBINARY(MAX)",
            [CanonicalType.Guid]     = "UNIQUEIDENTIFIER",
            [CanonicalType.Unknown]  = "NVARCHAR(MAX)",
        },
        [BackendType.Access] = new Dictionary<CanonicalType, string>
        {
            [CanonicalType.Text]     = "LONGTEXT",
            [CanonicalType.Boolean]  = "YESNO",
            [CanonicalType.Int32]    = "INTEGER",
            [CanonicalType.Int64]    = "LONG",         // Access LONG = 32-bit; no Int64
            [CanonicalType.Double]   = "DOUBLE",
            [CanonicalType.Decimal]  = "DECIMAL(18,4)",  // "DECIMAL" bez scale = DECIMAL(18,0) = integer u Accessu
            [CanonicalType.Date]     = "DATETIME",
            [CanonicalType.DateTime] = "DATETIME",
            [CanonicalType.Bytes]    = "LONGBINARY",
            [CanonicalType.Guid]     = "GUID",
            [CanonicalType.Unknown]  = "LONGTEXT",
        },
        [BackendType.Firebird] = new Dictionary<CanonicalType, string>
        {
            [CanonicalType.Text]     = "BLOB SUB_TYPE TEXT",
            [CanonicalType.Boolean]  = "BOOLEAN",
            [CanonicalType.Int32]    = "INTEGER",
            [CanonicalType.Int64]    = "BIGINT",
            [CanonicalType.Double]   = "DOUBLE PRECISION",
            [CanonicalType.Decimal]  = "DECIMAL(18,4)",
            [CanonicalType.Date]     = "DATE",
            [CanonicalType.DateTime] = "TIMESTAMP",
            [CanonicalType.Bytes]    = "BLOB",
            [CanonicalType.Guid]     = "CHAR(36)",
            [CanonicalType.Unknown]  = "BLOB SUB_TYPE TEXT",
        },
        [BackendType.DB2] = new Dictionary<CanonicalType, string>
        {
            [CanonicalType.Text]     = "CLOB(1M)",
            [CanonicalType.Boolean]  = "SMALLINT",     // DB2 has no native BOOLEAN
            [CanonicalType.Int32]    = "INTEGER",
            [CanonicalType.Int64]    = "BIGINT",
            [CanonicalType.Double]   = "DOUBLE",
            [CanonicalType.Decimal]  = "DECIMAL(18,4)",
            [CanonicalType.Date]     = "DATE",
            [CanonicalType.DateTime] = "TIMESTAMP",
            [CanonicalType.Bytes]    = "BLOB",
            [CanonicalType.Guid]     = "CHAR(36)",
            [CanonicalType.Unknown]  = "CLOB(1M)",
        },
        [BackendType.Oracle] = new Dictionary<CanonicalType, string>
        {
            [CanonicalType.Text]     = "CLOB",
            [CanonicalType.Boolean]  = "NUMBER(1,0)",  // Oracle has no BOOLEAN in SQL
            [CanonicalType.Int32]    = "NUMBER(10,0)",
            [CanonicalType.Int64]    = "NUMBER(19,0)",
            [CanonicalType.Double]   = "BINARY_DOUBLE",
            [CanonicalType.Decimal]  = "NUMBER(18,4)",
            [CanonicalType.Date]     = "TIMESTAMP",    // Oracle DATE has time; use TIMESTAMP for clarity
            [CanonicalType.DateTime] = "TIMESTAMP",
            [CanonicalType.Bytes]    = "BLOB",
            [CanonicalType.Guid]     = "RAW(16)",
            [CanonicalType.Unknown]  = "CLOB",
        },
        [BackendType.DBase] = new Dictionary<CanonicalType, string>
        {
            [CanonicalType.Text]     = "CHARACTER",
            [CanonicalType.Boolean]  = "LOGICAL",
            [CanonicalType.Int32]    = "INTEGER",
            [CanonicalType.Int64]    = "INTEGER",      // DBase has no Int64
            [CanonicalType.Double]   = "FLOAT",
            [CanonicalType.Decimal]  = "NUMERIC",
            [CanonicalType.Date]     = "DATE",
            [CanonicalType.DateTime] = "TIMESTAMP",
            [CanonicalType.Bytes]    = "CHARACTER",    // DBase has no binary type; fallback to text
            [CanonicalType.Guid]     = "CHARACTER",
            [CanonicalType.Unknown]  = "CHARACTER",
        },
    };

    // ── AutoNumber / identity DDL per backend ─────────────────────────────────
    // Used when Tippodatka = "AutoNumber" (or any identity-type string).
    // Each backend's auto-increment / identity column type string.

    private static readonly IReadOnlyDictionary<BackendType, string> AutoNumberDdl =
        new Dictionary<BackendType, string>
    {
        [BackendType.Access]    = "AUTOINCREMENT",
        [BackendType.SQLite]    = "INTEGER",
        [BackendType.SqlServer] = "INT IDENTITY(1,1)",
        [BackendType.PostgreSQL]= "SERIAL",
        [BackendType.MySQL]     = "INT AUTO_INCREMENT",
        [BackendType.Oracle]    = "NUMBER(10,0) GENERATED ALWAYS AS IDENTITY",
        [BackendType.DB2]       = "INT GENERATED ALWAYS AS IDENTITY",
        [BackendType.Firebird]  = "INTEGER GENERATED BY DEFAULT AS IDENTITY",
        [BackendType.DBase]     = "AUTOINC",
    };

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Maps a SQL type name from the given backend to a CanonicalType.
    /// Strips length/precision (e.g. "VARCHAR(255)" → looks up "VARCHAR").
    /// Returns CanonicalType.Unknown if no mapping found.
    /// </summary>
    public static CanonicalType Resolve(BackendType backend, string? sqlTypeName)
    {
        if (string.IsNullOrWhiteSpace(sqlTypeName)) return CanonicalType.Unknown;

        // Strip length/precision: "VARCHAR(255)" → "VARCHAR"
        var baseName = sqlTypeName.Trim();
        var parenIdx = baseName.IndexOf('(');
        if (parenIdx > 0) baseName = baseName[..parenIdx].TrimEnd();

        if (ToCanonical.TryGetValue(backend, out var map) &&
            map.TryGetValue(baseName, out var canonical))
            return canonical;

        return CanonicalType.Unknown;
    }

    /// <summary>
    /// Tries every known backend's canonical map until a match is found.
    /// Use this when the source of a type string is unknown or heterogeneous
    /// (e.g. Tippodatka may contain ADO names, SQLite types, Access DDL types, etc.).
    /// Returns CanonicalType.Unknown if no backend recognises the type.
    /// </summary>
    public static CanonicalType ResolveFromAny(string? sqlTypeName)
    {
        if (string.IsNullOrWhiteSpace(sqlTypeName)) return CanonicalType.Unknown;
        // Probe in the same priority order as ResolveToDdl
        foreach (var backend in new[]
        {
            BackendType.Access,     // first — ADO names live here
            BackendType.SQLite, BackendType.PostgreSQL, BackendType.MySQL,
            BackendType.SqlServer, BackendType.Firebird, BackendType.Oracle, BackendType.DB2,
            BackendType.DBase
        })
        {
            var ct = Resolve(backend, sqlTypeName);
            if (ct != CanonicalType.Unknown) return ct;
        }
        return CanonicalType.Unknown;
    }

    /// <summary>
    /// Returns the DDL type string for the given canonical type on the target backend.
    /// When maxLength > 0 and the canonical type is Text, appends (maxLength) to VARCHAR-style types.
    /// </summary>
    public static string GetDdlType(BackendType backend, CanonicalType canonical, int maxLength = 0)
    {
        if (!ToDdl.TryGetValue(backend, out var map))
            return "TEXT";

        if (!map.TryGetValue(canonical, out var ddl))
            ddl = map.GetValueOrDefault(CanonicalType.Unknown, "TEXT");

        // For text with a known length, use VARCHAR(n) instead of unbounded type
        if (canonical == CanonicalType.Text && maxLength > 0)
        {
            return backend switch
            {
                BackendType.PostgreSQL => $"VARCHAR({maxLength})",
                BackendType.MySQL      => $"VARCHAR({maxLength})",
                BackendType.SqlServer  => $"NVARCHAR({maxLength})",
                BackendType.Firebird   => $"VARCHAR({Math.Min(maxLength, 32765)})",
                BackendType.SQLite     => "TEXT",
                BackendType.Access     => maxLength <= 255 ? $"VARCHAR({maxLength})" : "LONGTEXT",
                BackendType.Oracle     => $"VARCHAR2({Math.Min(maxLength, 4000)})",
                BackendType.DB2        => $"VARCHAR({Math.Min(maxLength, 32672)})",
                BackendType.DBase      => $"CHARACTER({Math.Min(maxLength, 254)})",
                _                      => ddl
            };
        }

        return ddl;
    }

    /// <summary>
    /// Resolves a SQL type string (including ADO names and "AutoNumber") to the target backend's DDL type.
    /// Access / ADO types are tried first, so "adDouble", "adWChar", "AutoNumber" etc. always resolve.
    /// Use this in CreateTable / AddColumn for the column type resolution.
    /// </summary>
    public static string ResolveToDdl(BackendType target, string? sourceType, int maxLength = 0)
    {
        if (string.IsNullOrWhiteSpace(sourceType))
            return GetDdlType(target, CanonicalType.Text, maxLength);

        // AutoNumber / identity columns → backend-specific identity syntax (before canonical lookup)
        if (IsAutoNumberType(sourceType.Trim()))
            return AutoNumberDdl.TryGetValue(target, out var idDdl) ? idDdl : GetDdlType(target, CanonicalType.Int32, 0);

        // Try Access first (ADO names live here), then all other backends
        var tryOrder = new[]
        {
            BackendType.Access,
            BackendType.SQLite, BackendType.PostgreSQL, BackendType.MySQL,
            BackendType.SqlServer, BackendType.Firebird, BackendType.Oracle, BackendType.DB2,
            BackendType.DBase
        };

        foreach (var backend in tryOrder)
        {
            var canonical = Resolve(backend, sourceType);
            if (canonical != CanonicalType.Unknown)
                return GetDdlType(target, canonical, maxLength);
        }

        // Unrecognized type — fall back to target's Unknown DDL type (usually unbounded text/blob)
        return GetDdlType(target, CanonicalType.Unknown, maxLength);
    }

    /// <summary>
    /// Returns true if the type string represents an auto-increment / identity / sequence column.
    /// Covers: "AutoNumber" (Blueprint/ADO), "AUTOINCREMENT" (Access), "AUTOINC" (dBase),
    /// "SERIAL" (PostgreSQL), "INT AUTO_INCREMENT" (MySQL), SQL Server / DB2 / Oracle IDENTITY / GENERATED syntax.
    /// </summary>
    public static bool IsAutoNumberType(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        var t = s.Trim();
        return t.Equals("AutoNumber",    StringComparison.OrdinalIgnoreCase) ||
               t.Equals("AUTOINCREMENT", StringComparison.OrdinalIgnoreCase) ||
               t.Equals("AUTOINC",       StringComparison.OrdinalIgnoreCase) ||
               t.Equals("SERIAL",        StringComparison.OrdinalIgnoreCase) ||
               t.Contains("IDENTITY",    StringComparison.OrdinalIgnoreCase) ||
               t.Contains("GENERATED",   StringComparison.OrdinalIgnoreCase) ||
               t.Contains("AUTO_INCREMENT", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Maps a CanonicalType to the closest ADO type name from the Matrix TipPodatka vocabulary.
    /// Used by Schema Import to store a normalised type in Kolone.Tippodatka.
    /// Text columns: maxLength ≤ 255 → adWChar; maxLength 0 or > 255 → adMemo.
    /// </summary>
    public static string CanonicalToAdo(CanonicalType canonical, int maxLength = 0) => canonical switch
    {
        CanonicalType.Boolean  => "adBoolean",
        CanonicalType.Int32    => "adLong",
        CanonicalType.Int64    => "adLong",         // No 64-bit in the TipPodatka list; clamp to adLong
        CanonicalType.Double   => "adDouble",
        CanonicalType.Decimal  => "adDecimal",
        CanonicalType.Date     => "adDate",
        CanonicalType.DateTime => "adDate",
        CanonicalType.Bytes    => "adBinary",
        CanonicalType.Guid     => "adGUID",
        CanonicalType.Text     => (maxLength > 0 && maxLength <= 255) ? "adWChar" : "adMemo",
        _                      => "adVariant",      // Unknown → adVariant (pass-through)
    };

    /// <summary>
    /// Normalises any backend-specific SQL type string to an ADO type name from the
    /// Matrix TipPodatka vocabulary.  Called during Schema Import so that
    /// Kolone.Tippodatka always holds one of the 15 well-known ADO types.
    /// </summary>
    public static string NormalizeToAdo(string? sqlType, int maxLength = 0)
    {
        if (string.IsNullOrWhiteSpace(sqlType)) return "adVariant";

        var trimmed = sqlType.Trim();

        // Already one of the 15 ADO types — keep as-is
        if (_adoTypes.Contains(trimmed)) return trimmed;

        // Identity / auto-increment → AutoNumber
        if (IsAutoNumberType(trimmed)) return "AutoNumber";

        // Resolve via canonical type (tries Access map first, so ADO names always match)
        var canonical = ResolveFromAny(trimmed);
        return CanonicalToAdo(canonical, maxLength);
    }

    private static readonly HashSet<string> _adoTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AutoNumber", "adWChar", "adMemo", "adLong", "adInteger", "adDouble", "adSingle",
        "adDecimal", "adCurrency", "adBoolean", "adDate", "adGUID", "adBinary", "adOLE", "adVariant"
    };

    // ── Canonical value converters ────────────────────────────────────────────
    // Used by DatabaseTransferService to normalize source values to canonical C# types.

    public static object? ToCanonicalValue(object? val, CanonicalType type)
    {
        if (val == null || val is DBNull) return null;

        return type switch
        {
            CanonicalType.Boolean  => AsBoolean(val),
            CanonicalType.Int32    => AsInt32(val),
            CanonicalType.Int64    => AsInt64(val),
            CanonicalType.Double   => AsDouble(val),
            CanonicalType.Decimal  => AsDecimal(val),
            CanonicalType.Text     => val.ToString(),
            CanonicalType.Date     => AsDateOnly(val),
            CanonicalType.DateTime => AsDateTime(val),
            CanonicalType.Bytes    => val as byte[],
            CanonicalType.Guid     => AsGuid(val),
            _                      => val    // Unknown: pass through unchanged
        };
    }

    private static bool AsBoolean(object val) => val switch
    {
        bool b   => b,
        long n   => n != 0,
        int n    => n != 0,
        short n  => n != 0,
        byte n   => n != 0,
        double d => d != 0,
        string s => s == "1" || s == "-1"
                              || s.Equals("true",  StringComparison.OrdinalIgnoreCase)
                              || s.Equals("yes",   StringComparison.OrdinalIgnoreCase)
                              || s.Equals("t",     StringComparison.OrdinalIgnoreCase)
                              || s.Equals(".t.",   StringComparison.OrdinalIgnoreCase),
        _        => TryConvertBoolean(val)
    };

    private static bool TryConvertBoolean(object val)
    {
        try { return Convert.ToBoolean(val); }
        catch
        {
            LogService.Warning("TypeMappings", $"AsBoolean: unhandled type {val.GetType().Name} value='{val}', defaulting to false");
            return false;
        }
    }

    private static int AsInt32(object val) => val switch
    {
        int n      => n,
        long n     => (int)Math.Clamp(n, int.MinValue, int.MaxValue),
        bool b     => b ? 1 : 0,
        string s   => int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var r) ? r : 0,
        _          => Convert.ToInt32(val)
    };

    private static long AsInt64(object val) => val switch
    {
        long n     => n,
        int n      => n,
        bool b     => b ? 1L : 0L,
        string s   => long.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var r) ? r : 0L,
        _          => Convert.ToInt64(val)
    };

    private static double AsDouble(object val) => val switch
    {
        double d   => d,
        float f    => f,
        string s   => double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var r) ? r
                    : double.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture,   out var r2) ? r2
                    : 0,
        _          => Convert.ToDouble(val, CultureInfo.InvariantCulture)
    };

    private static decimal AsDecimal(object val) => val switch
    {
        decimal d  => d,
        string s   => decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var r) ? r
                    : decimal.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture,   out var r2) ? r2
                    : 0m,
        _          => Convert.ToDecimal(val, CultureInfo.InvariantCulture)
    };

    // Common date/datetime string formats encountered in legacy databases
    private static readonly string[] _dateFormats =
    [
        "yyyy-MM-dd",
        "dd.MM.yyyy", "d.M.yyyy",
        "dd/MM/yyyy", "MM/dd/yyyy",
        "yyyy.MM.dd",
    ];
    private static readonly string[] _dateTimeFormats =
    [
        "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss.fff",
        "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.fff",
        "yyyy-MM-dd",
        "dd.MM.yyyy HH:mm:ss", "d.M.yyyy HH:mm:ss",
        "dd.MM.yyyy", "d.M.yyyy",
        "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy",
        "MM/dd/yyyy HH:mm:ss", "MM/dd/yyyy",
    ];

    private static DateOnly AsDateOnly(object val) => val switch
    {
        DateOnly d   => d,
        DateTime dt  => DateOnly.FromDateTime(dt),
        // OLE Automation Date stored as double (Access/Excel internal format)
        double d     => DateOnly.FromDateTime(DateTime.FromOADate(d)),
        long n       => DateOnly.FromDateTime(DateTime.FromOADate(n)),
        string s     => ParseDateOnly(s),
        _            => ParseDateOnly(val.ToString() ?? "")
    };

    private static DateTime AsDateTime(object val) => val switch
    {
        DateTime dt  => dt,
        DateOnly d   => d.ToDateTime(TimeOnly.MinValue),
        // OleDb DBTime (type code 134) returns TimeSpan — treat as time-only, date part = MinValue
        TimeSpan ts  => DateTime.MinValue.Date.Add(ts),
        // OLE Automation Date stored as double (Access/Excel internal format)
        double d     => DateTime.FromOADate(d),
        long n       => DateTime.FromOADate(n),
        string s     => ParseDateTime(s),
        _            => ParseDateTime(val.ToString() ?? "")
    };

    private static DateOnly ParseDateOnly(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return DateOnly.MinValue;

        // Try as date-only string
        if (DateOnly.TryParseExact(s, _dateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d1))
            return d1;
        if (DateOnly.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out var d2))
            return d2;

        // String may include a time component (e.g. "15.01.2023 00:00:00") — extract date part
        if (DateTime.TryParseExact(s, _dateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt1))
            return DateOnly.FromDateTime(dt1);
        if (DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out var dt2))
            return DateOnly.FromDateTime(dt2);

        LogService.Warning("TypeMappings", $"Could not parse date value: '{s}' — storing as MinValue");
        return DateOnly.MinValue;
    }

    private static DateTime ParseDateTime(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return DateTime.MinValue;

        if (DateTime.TryParseExact(s, _dateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt1))
            return dt1;
        if (DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out var dt2))
            return dt2;

        LogService.Warning("TypeMappings", $"Could not parse datetime value: '{s}' — storing as MinValue");
        return DateTime.MinValue;
    }

    private static Guid AsGuid(object val) => val switch
    {
        Guid g     => g,
        string s   => Guid.TryParse(s, out var g) ? g : Guid.Empty,
        byte[] b   => b.Length == 16 ? new Guid(b) : Guid.Empty,
        _          => Guid.TryParse(val.ToString(), out var g) ? g : Guid.Empty
    };
}
