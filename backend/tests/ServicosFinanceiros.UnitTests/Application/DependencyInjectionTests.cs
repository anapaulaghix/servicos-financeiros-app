using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ServicosFinanceiros.Application;
using ServicosFinanceiros.Application.Transactions;

namespace ServicosFinanceiros.UnitTests.Application;

public class DependencyInjectionTests
{
    [Fact]
    public void AddApplication_ShouldRegisterTheUseCasePerRequest()
    {
        var services = new ServiceCollection().AddApplication();

        services.Should().ContainSingle(d => d.ServiceType == typeof(IProcessTransactionHandler))
            .Which.Should().Match<ServiceDescriptor>(d =>
                d.ImplementationType == typeof(ProcessTransactionHandler) && d.Lifetime == ServiceLifetime.Scoped);
    }
}
