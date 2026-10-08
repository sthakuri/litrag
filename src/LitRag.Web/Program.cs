using LitRag.Web.Components;
using LitRag.Web.Data;
using LitRag.Web.Services.Library;
using LitRag.Web.Services.Ollama;
using LitRag.Web.Services.Pdf;
using LitRag.Web.Services.Rag;
using LitRag.Web.Services.Summary;
using LitRag.Web.Services.Validation;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Uploaded PDFs can be several MB; raise the Blazor circuit's SignalR message size limit.
builder.Services.Configure<Microsoft.AspNetCore.SignalR.HubOptions>(options =>
{
    options.MaximumReceiveMessageSize = 50 * 1024 * 1024;
});

var dataDir = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(dataDir);

var sqliteConnectionString = builder.Configuration.GetConnectionString("Default") ?? "Data Source=App_Data/litrag.db";
builder.Services.AddDbContext<LitRagDbContext>(options => options.UseSqlite(sqliteConnectionString));

builder.Services.Configure<OllamaOptions>(builder.Configuration.GetSection(OllamaOptions.SectionName));
builder.Services.AddHttpClient<OllamaClient>((sp, client) =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OllamaOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = Timeout.InfiniteTimeSpan; // streaming chat responses can run long; per-call timeouts are applied where needed
});

builder.Services.AddSingleton<PdfTextExtractor>();
builder.Services.AddSingleton<TextChunker>();
builder.Services.AddScoped<EmbeddingService>();
builder.Services.AddScoped<PaperValidationService>();
builder.Services.AddScoped<LibraryService>();
builder.Services.AddScoped<RagChatService>();
builder.Services.AddScoped<RelatedPapersService>();
builder.Services.AddScoped<SummaryService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LitRagDbContext>();
    db.Database.EnsureCreated();
    ApplyLightweightSchemaUpgrades(sqliteConnectionString);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

// Serves wwwroot dynamically (needed for PDFs uploaded to wwwroot/library at runtime,
// which MapStaticAssets below won't see since it only knows about build-time assets).
app.UseStaticFiles();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

// EnsureCreated() only builds a brand-new database; it won't add columns that were added to an
// entity after a database already exists on disk. Rather than require a full EF migrations setup
// (or force users to delete their library) for small additive changes, patch missing columns in
// directly. Existing rows get the column's default value.
static void ApplyLightweightSchemaUpgrades(string connectionString)
{
    // (column name, DDL to add it if missing)
    (string Column, string AlterSql)[] paperColumnUpgrades =
    [
        ("ReadingStatus", "ALTER TABLE Papers ADD COLUMN ReadingStatus INTEGER NOT NULL DEFAULT 0"),
        ("Summary", "ALTER TABLE Papers ADD COLUMN Summary TEXT NULL"),
    ];

    using var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
    connection.Open();

    var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    using (var checkCommand = connection.CreateCommand())
    {
        checkCommand.CommandText = "PRAGMA table_info(Papers)";
        using var reader = checkCommand.ExecuteReader();
        while (reader.Read())
        {
            existingColumns.Add(reader.GetString(1));
        }
    }

    foreach (var (column, alterSql) in paperColumnUpgrades)
    {
        if (!existingColumns.Contains(column))
        {
            using var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = alterSql;
            alterCommand.ExecuteNonQuery();
        }
    }
}
