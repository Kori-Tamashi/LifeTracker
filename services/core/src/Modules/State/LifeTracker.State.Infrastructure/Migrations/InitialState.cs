using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LifeTracker.State.Infrastructure.Migrations;

[DbContext(typeof(StateDbContext))]
[Migration("202611200001_InitialState")]
public sealed class InitialState : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        using var stream = typeof(InitialState).Assembly.GetManifestResourceStream(
            "LifeTracker.State.Infrastructure.Migrations.InitialState.sql")
            ?? throw new InvalidOperationException("Migration resource is missing.");
        using var reader = new StreamReader(stream);
        migrationBuilder.Sql(reader.ReadToEnd());
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Destructive downgrade is unsupported; restore a verified snapshot.");

    protected override void BuildTargetModel(ModelBuilder modelBuilder) =>
        modelBuilder.HasAnnotation("ProductVersion", "10.0.15");
}

[DbContext(typeof(StateDbContext))]
public sealed class StateDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder) =>
        modelBuilder.HasAnnotation("ProductVersion", "10.0.15");
}
