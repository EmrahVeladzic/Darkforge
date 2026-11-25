using System.Net.NetworkInformation;
using backend.Controllers;
using backend.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

if (IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(e => e.Port == AppController.PORT))
{
    using (HttpClient client = new HttpClient())
    {
        await client.GetAsync(AppController.URL + "/api/App/open");
    }
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls(AppController.URL);

// Add services to the container.

builder.Services.AddCors(options =>
{
    options.AddPolicy(name: "ALL", policy =>
    {

        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();

    });


});

builder.Services.AddControllers();

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = null;
});

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDbContext<DarkforgeDBContext>(options =>
{

    options.UseSqlite(DarkforgeDBContext.ConnectionString);

});


builder.Services.AddHostedService<CleanupService>();

builder.Services.AddHostedService<TabWatchService>();


if (!File.Exists(DarkforgeDBContext.DBPath))
{
    using Stream script = typeof(Program).Assembly.GetManifestResourceStream("DarkforgeDB.sql")!;
    using StreamReader reader = new StreamReader(script);
    using DarkforgeDBContext ctx = new DarkforgeDBContext();
    ctx.Database.ExecuteSqlRaw(reader.ReadToEnd());
}

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

}

app.UseRouting();

app.UseCors("ALL");


app.MapControllers();

app.MapFallback("{**path}", EmbeddedFrontend.Serve);

app.Run();