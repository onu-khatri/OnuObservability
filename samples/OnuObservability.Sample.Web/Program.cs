using System.Diagnostics;
using OnuObservability.AspNetCore;
using OnuObservability.Hosting.Composition;
using OnuObservability.Sample.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOnuObservability(builder.Configuration)
    // Outbound HTTP tracing is opt-in. The predicate is the "reviewed finite request
    // predicate": only non-loopback (external dependency) calls are instrumented, and
    // the package strips raw URL/path/query/host/port/authorization tags before export.
    .AddOutboundHttpClientInstrumentation(static request =>
        request.RequestUri is { } uri && !uri.IsLoopback)
    // Application-owned domain schema: operations, outcomes, errors, and reasons.
    .AddOrderTelemetry()
    // The ASP.NET Core adapter (request enrichment + bounded route policy).
    .AddOnuAspNetCore();

// A downstream dependency client. Point BaseAddress at a real inventory service to
// observe the success path; leave it unreachable to observe graceful dependency-outage
// classification in the checkout endpoint.
builder.Services.AddHttpClient("inventory", client =>
    client.BaseAddress = new Uri(builder.Configuration["Inventory:BaseAddress"] ?? "http://localhost:9000"));
builder.Services.AddSingleton<CheckoutService>();

var app = builder.Build();

app.UseOnuObservability();

app.MapGet("/", () => "Hello World!");
app.MapGet("/correlation", () => Results.Ok(new
{
    TraceId = Activity.Current?.TraceId.ToHexString(),
    SpanId = Activity.Current?.SpanId.ToHexString(),
}));
app.MapGet("/users/{id}", (string id) => Results.Ok(new { Id = id, Found = true }));
app.MapGet("/validation", () => Results.BadRequest());
app.MapGet("/failure", () => Results.Problem(statusCode: StatusCodes.Status500InternalServerError));

// Excluded from adapter telemetry via OnuObservability:AspNetCore:ExcludedRouteTemplates.
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

// Demonstrates a not-found outcome (404 -> not_found).
app.MapGet("/products/{id}", (string id) =>
    id == "known"
        ? Results.Ok(new { Id = id })
        : Results.NotFound());

// Demonstrates a request-scoped domain operation with bounded custom fields.
app.MapPost("/orders/checkout", async (
    CheckoutRequest request,
    CheckoutService checkout,
    CancellationToken cancellationToken) =>
{
    var outcome = await checkout.CheckoutAsync(request, cancellationToken);
    return outcome switch
    {
        CheckoutOutcome.Completed => Results.Ok(new { status = "completed" }),
        _ => Results.Conflict(new { status = "out_of_stock" }),
    };
});

app.MapPost("/shutdown", (IHostApplicationLifetime lifetime) =>
{
    lifetime.StopApplication();
    return Results.Accepted();
});

app.Run();
