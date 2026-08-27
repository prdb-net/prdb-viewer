using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace Prdb.Viewer.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ViewerDbContext))]
internal partial class ViewerDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder.HasAnnotation("ProductVersion", "10.0.10");
#pragma warning restore 612, 618
    }
}
