using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SnmpCollector.Extensions;
using SnmpCollector.Pipeline;

var builder = Host.CreateApplicationBuilder(args);

// DI registration order: Telemetry FIRST (registered first = disposed last = ForceFlush on shutdown)
builder.AddSnmpTelemetry();
builder.Services.AddSnmpConfiguration(builder.Configuration);
builder.Services.AddSnmpPipeline();
builder.Services.AddSnmpScheduling(builder.Configuration);

var host = builder.Build();

// Seed first correlation ID before any Quartz job fires (before RunAsync starts hosted services)
var correlationService = host.Services.GetRequiredService<ICorrelationService>();
correlationService.SetCorrelationId(Guid.NewGuid().ToString("N"));

try
{
    await host.RunAsync();
}
catch (OptionsValidationException ex)
{
    // Fail-fast: surface all validation failures clearly before the host accepts work
    Console.Error.WriteLine("Configuration validation failed:");
    foreach (var failure in ex.Failures)
    {
        Console.Error.WriteLine($"  - {failure}");
    }

    throw;
}
