using Microsoft.EntityFrameworkCore;

namespace LifeTracker.Infrastructure.Shared;

// Shared reviewed migrations own the physical schema. Typed repositories live in their modules.
// Do not call EnsureCreated: the reviewed migration owns deferred FKs and STRICT tables.
public sealed class CoreDbContext(DbContextOptions<CoreDbContext> options) : DbContext(options);
