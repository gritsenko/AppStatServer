using AppStatServerLite;
using AppStatServerLite.Sentry;

var builder = WebApplication.CreateSlimBuilder(args);

var dbFileName = builder.Configuration["LiteDbFilePath"];
builder.Services.AddSingleton<IEventStorage, LiteDbEventStorage>(services => new LiteDbEventStorage(dbFileName));

//builder.Services.ConfigureHttpJsonOptions(options =>
//{
//    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
//});

var app = builder.Build();

var todosApi = app.MapGroup("/events");
todosApi.MapGet("/", (IEventStorage es) => es.GetRecentEventsAsync());
todosApi.MapGet("/{id}", async (string id, IEventStorage es) =>
    (await es.GetRecentEventsAsync()).FirstOrDefault(a => a.Id == id) is { } todo
        ? Results.Ok(todo)
        : Results.NotFound());

_ = new EnvelopeHandler(app);

app.Run();