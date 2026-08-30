using Ocelot.DependencyInjection;
using Ocelot.Middleware;

// The whole gateway, in eleven lines -- routing, aggregation-if-you-add-it,
// and cross-cutting policy all live in ocelot.json, not in this file. Note
// what's genuinely absent here on purpose: no per-client custom C# routing
// logic, which is precisely the thing Section 02 warns turns a "thin"
// gateway into an accidental monolith. If a real need for custom
// aggregation or auth-token transformation shows up, Ocelot has extension
// points for it -- but reaching for them is a deliberate escalation, not
// the starting point.
var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("ocelot.json", optional: false, reloadOnChange: true);
builder.Services.AddOcelot(builder.Configuration);

var app = builder.Build();
await app.UseOcelot();
app.Run();
