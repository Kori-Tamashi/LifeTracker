using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LifeTracker.Infrastructure.Shared.Migrations;

[DbContext(typeof(CoreDbContext))]
[Migration("202611200002_DurableJobs")]
public sealed class DurableJobs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        using var stream = typeof(DurableJobs).Assembly.GetManifestResourceStream(
            "LifeTracker.Infrastructure.Shared.Migrations.DurableJobs.sql")
            ?? throw new InvalidOperationException("Migration resource is missing.");
        using var reader = new StreamReader(stream);
        migrationBuilder.Sql(reader.ReadToEnd());
    }
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Restore a verified snapshot instead of destructive downgrade.");
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => modelBuilder.HasAnnotation("ProductVersion", "10.0.15");
}
