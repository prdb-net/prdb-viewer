using Microsoft.EntityFrameworkCore;

namespace Prdb.Viewer.Infrastructure.Persistence;

public sealed class ViewerDbContext(DbContextOptions<ViewerDbContext> options) : DbContext(options)
{
}
