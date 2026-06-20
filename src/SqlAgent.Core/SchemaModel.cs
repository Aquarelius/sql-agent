namespace SqlAgent.Core;

/// <summary>Provider-neutral description of a database's structure (CD-50 T4).</summary>
public record DatabaseSchema(IReadOnlyList<SchemaTable> Tables);

/// <summary>One base table, with its columns, primary-key column order, and outgoing foreign keys.</summary>
public record SchemaTable(
    string Schema,
    string Name,
    IReadOnlyList<SchemaColumn> Columns,
    IReadOnlyList<string> PrimaryKey,
    IReadOnlyList<ForeignKey> ForeignKeys);

/// <summary>
/// One column. <paramref name="MaxLength"/> (character/binary length; -1 means MAX),
/// <paramref name="Precision"/>, and <paramref name="Scale"/> are the raw catalog sizing values,
/// null when the type carries no such facet (e.g. <c>int</c> has no length, <c>varchar</c> no scale).
/// </summary>
public record SchemaColumn(
    string Name, string DataType, bool IsNullable,
    int? MaxLength = null, int? Precision = null, int? Scale = null);

/// <summary>A column pointing at another table — the "basic relationship" the model carries.</summary>
public record ForeignKey(string Column, string ReferencedSchema, string ReferencedTable, string ReferencedColumn);

/// <summary>
/// Assembles and filters the common <see cref="DatabaseSchema"/>. The provider drivers run their
/// dialect SQL and feed the flat rows here; the assembly/filter logic is dialect-free and unit-tested.
/// </summary>
public static class SchemaModel
{
    /// <summary>
    /// Builds a schema from flat catalog rows. Column and primary-key order is preserved as supplied,
    /// so callers must ORDER BY ordinal position. Rows for unknown tables are grouped on (schema, name).
    /// </summary>
    public static DatabaseSchema Build(
        IEnumerable<(string Schema, string Table, string Column, string DataType, bool Nullable,
            int? MaxLength, int? Precision, int? Scale)> columns,
        IEnumerable<(string Schema, string Table, string Column)> primaryKeys,
        IEnumerable<(string Schema, string Table, string Column, string RefSchema, string RefTable, string RefColumn)> foreignKeys)
    {
        var pkByTable = primaryKeys
            .GroupBy(p => (p.Schema, p.Table))
            .ToDictionary(g => g.Key, g => g.Select(p => p.Column).ToList());

        var fkByTable = foreignKeys
            .GroupBy(f => (f.Schema, f.Table))
            .ToDictionary(
                g => g.Key,
                g => g.Select(f => new ForeignKey(f.Column, f.RefSchema, f.RefTable, f.RefColumn)).ToList());

        var tables = columns
            .GroupBy(c => (c.Schema, c.Table))
            .Select(g => new SchemaTable(
                g.Key.Schema,
                g.Key.Table,
                g.Select(c => new SchemaColumn(c.Column, c.DataType, c.Nullable, c.MaxLength, c.Precision, c.Scale)).ToList(),
                pkByTable.GetValueOrDefault(g.Key, []),
                fkByTable.GetValueOrDefault(g.Key, [])))
            .ToList();

        return new DatabaseSchema(tables);
    }

    /// <summary>
    /// Drops tables the policy says are invisible (CD-50 visibility). Foreign keys that point at a
    /// now-hidden table are also dropped, so a hidden table's name never leaks through a relationship.
    /// </summary>
    public static DatabaseSchema Filter(DatabaseSchema schema, Func<string, string, bool> isVisible)
    {
        var visible = schema.Tables.Where(t => isVisible(t.Schema, t.Name)).ToList();
        var kept = visible.Select(t => (t.Schema, t.Name)).ToHashSet();

        var filtered = visible
            .Select(t => t with
            {
                ForeignKeys = t.ForeignKeys
                    .Where(fk => kept.Contains((fk.ReferencedSchema, fk.ReferencedTable)))
                    .ToList(),
            })
            .ToList();

        return new DatabaseSchema(filtered);
    }
}
