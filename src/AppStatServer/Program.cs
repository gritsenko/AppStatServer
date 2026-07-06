using AppStatServer;
using AppStatServer.Sentry;

var builder = WebApplication.CreateSlimBuilder(args);

var dbFileName = builder.Configuration["LiteDbFilePath"];
builder.Services.AddSingleton<IEventStorage, LiteDbEventStorage>(services => new LiteDbEventStorage(dbFileName));

//builder.Services.ConfigureHttpJsonOptions(options =>
//{
//    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
//});

var app = builder.Build();

var eventsApi = app.MapGroup("/events");
eventsApi.MapGet("/", (IEventStorage es) => es.GetRecentEventsAsync());
eventsApi.MapGet("/{id}", async (string id, IEventStorage es) =>
    (await es.GetRecentEventsAsync()).FirstOrDefault(a => a.Id == id) is { } ev
        ? Results.Ok(ev)
        : Results.NotFound());

var sessionsApi = app.MapGroup("/sessions");
sessionsApi.MapGet("/", (IEventStorage es) => es.GetRecentSessionsAsync());

_ = new EnvelopeHandler(app);

app.Run();