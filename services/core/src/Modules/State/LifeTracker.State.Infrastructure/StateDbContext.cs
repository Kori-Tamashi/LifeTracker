using Microsoft.EntityFrameworkCore;

namespace LifeTracker.State.Infrastructure;

// D-004.1 is a schema/bootstrap slice. Domain row mappings and repository follow in D-004.2.
// Do not call EnsureCreated: the reviewed migration owns deferred FKs and STRICT tables.
public sealed class StateDbContext(DbContextOptions<StateDbContext> options) : DbContext(options);
