using LitRag.Web.Components;
using LitRag.Web.Data;
using LitRag.Web.Services.Library;
using LitRag.Web.Services.Ollama;
using LitRag.Web.Services.Pdf;
using LitRag.Web.Services.Rag;
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

builder.Services.AddDbContext<LitRagDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=App_Data/litrag.db"));

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

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LitRagDbContext>();
    db.Database.EnsureCreated();
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
