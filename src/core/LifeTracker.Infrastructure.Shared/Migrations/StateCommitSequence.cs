using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LifeTracker.Infrastructure.Shared.Migrations;

[DbContext(typeof(CoreDbContext))]
[Migration("202611200003_StateCommitSequence")]
public sealed class StateCommitSequence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE state_commits (
            sequence INTEGER PRIMARY KEY AUTOINCREMENT,
            profile_id TEXT NOT NULL, entity_id TEXT NOT NULL, revision INTEGER NOT NULL,
            UNIQUE(profile_id,entity_id,revision),
            FOREIGN KEY(profile_id,entity_id,revision) REFERENCES revisions(profile_id,entity_id,revision)
        ) STRICT;
        INSERT INTO state_commits(profile_id,entity_id,revision)
        SELECT profile_id,entity_id,revision FROM observations ORDER BY profile_id,entity_id,revision;
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Restore a verified snapshot instead of destructive downgrade.");
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => modelBuilder.HasAnnotation("ProductVersion", "10.0.15");
}
