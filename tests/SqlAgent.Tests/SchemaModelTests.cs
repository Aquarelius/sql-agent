using SqlAgent.Core;

namespace SqlAgent.Tests;

public class SchemaModelBuildTests
{
    [Fact]
    public void Build_groups_columns_pks_and_fks_per_table_preserving_order()
    {
        var schema = SchemaModel.Build(
            columns:
            [
                ("dbo", "Orders", "Id", "int", false),
                ("dbo", "Orders", "CustomerId", "int", false),
                ("dbo", "Customers", "Id", "int", false),
                ("dbo", "Customers", "Name", "nvarchar", true),
            ],
            primaryKeys:
            [
                ("dbo", "Orders", "Id"),
                ("dbo", "Customers", "Id"),
            ],
            foreignKeys:
            [
                ("dbo", "Orders", "CustomerId", "dbo", "Customers", "Id"),
            ]);

        var orders = schema.Tables.Single(t => t.Name == "Orders");
        Assert.Equal(["Id", "CustomerId"], orders.Columns.Select(c => c.Name));
        Assert.Equal(["Id"], orders.PrimaryKey);
        var fk = Assert.Single(orders.ForeignKeys);
        Assert.Equal(("CustomerId", "dbo", "Customers", "Id"),
            (fk.Column, fk.ReferencedSchema, fk.ReferencedTable, fk.ReferencedColumn));

        var customers = schema.Tables.Single(t => t.Name == "Customers");
        Assert.Empty(customers.ForeignKeys);
        Assert.True(customers.Columns.Single(c => c.Name == "Name").IsNullable);
    }

    [Fact]
    public void Build_maps_a_composite_fk_as_ordered_column_pairs()
    {
        // Each local column pairs with the referenced column at the same position — never a cross product.
        var schema = SchemaModel.Build(
            columns: [("dbo", "OrderLine", "OrderId", "int", false), ("dbo", "OrderLine", "Sku", "varchar", false)],
            primaryKeys: [],
            foreignKeys:
            [
                ("dbo", "OrderLine", "OrderId", "dbo", "OrderItem", "OrderId"),
                ("dbo", "OrderLine", "Sku", "dbo", "OrderItem", "Sku"),
            ]);

        var fks = schema.Tables.Single().ForeignKeys;
        Assert.Equal(2, fks.Count);
        Assert.Equal(
            [("OrderId", "OrderId"), ("Sku", "Sku")],
            fks.Select(f => (f.Column, f.ReferencedColumn)));
    }

    [Fact]
    public void Build_handles_a_table_with_no_keys()
    {
        var schema = SchemaModel.Build(
            columns: [("public", "logs", "msg", "text", true)],
            primaryKeys: [],
            foreignKeys: []);

        var t = Assert.Single(schema.Tables);
        Assert.Empty(t.PrimaryKey);
        Assert.Empty(t.ForeignKeys);
    }
}

public class SchemaModelFilterTests
{
    private static DatabaseSchema TwoTables() => SchemaModel.Build(
        columns:
        [
            ("dbo", "Public", "Id", "int", false),
            ("dbo", "Secret", "Id", "int", false),
        ],
        primaryKeys: [],
        foreignKeys:
        [
            ("dbo", "Public", "SecretId", "dbo", "Secret", "Id"),
        ]);

    [Fact]
    public void Filter_omits_invisible_tables()
    {
        var filtered = SchemaModel.Filter(TwoTables(), (_, table) => table != "Secret");

        var t = Assert.Single(filtered.Tables);
        Assert.Equal("Public", t.Name);
    }

    [Fact]
    public void Filter_drops_fks_pointing_at_a_hidden_table_so_its_name_never_leaks()
    {
        var filtered = SchemaModel.Filter(TwoTables(), (_, table) => table != "Secret");

        Assert.Empty(filtered.Tables.Single().ForeignKeys);
    }

    [Fact]
    public void Filter_keeps_everything_when_all_visible()
    {
        var filtered = SchemaModel.Filter(TwoTables(), (_, _) => true);

        Assert.Equal(2, filtered.Tables.Count);
        Assert.Single(filtered.Tables.Single(t => t.Name == "Public").ForeignKeys);
    }
}
