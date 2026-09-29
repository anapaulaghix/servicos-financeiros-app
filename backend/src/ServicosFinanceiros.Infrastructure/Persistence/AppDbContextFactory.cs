using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ServicosFinanceiros.Infrastructure.Persistence;

/// <summary>
/// Usada apenas pelas ferramentas do EF (`dotnet ef migrations ...`). Gerar migrations não abre
/// conexão com o banco, então esta connection string é só um valor de design-time, sem credenciais reais.
/// </summary>
internal sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=design_time")
            .Options;

        return new AppDbContext(options);
    }
}
