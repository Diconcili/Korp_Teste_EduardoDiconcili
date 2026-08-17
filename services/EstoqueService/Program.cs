using EstoqueService.Data;
using EstoqueService.Models;
using EstoqueService.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var dataDirectory = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "data"));
Directory.CreateDirectory(dataDirectory);
var databasePath = Path.Combine(dataDirectory, "estoque.db");
var legacyDatabasePath = Path.Combine(builder.Environment.ContentRootPath, "estoque.db");
if (!File.Exists(databasePath) && File.Exists(legacyDatabasePath)) File.Move(legacyDatabasePath, databasePath);

builder.Services.AddDbContext<StockDb>(options => options.UseSqlite($"Data Source={databasePath}"));
builder.Services.AddScoped<InventoryService>();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddOpenApi();

var app = builder.Build();
app.UseCors();
using (var scope = app.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<StockDb>().Database.EnsureCreatedAsync();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "estoque" }));
app.MapGet("/api/products", async (StockDb db) => await db.Products.OrderBy(product => product.Code).ToListAsync());
app.MapPost("/api/products", async (CreateProduct input, StockDb db) =>
{
    if (string.IsNullOrWhiteSpace(input.Code) || string.IsNullOrWhiteSpace(input.Description) || input.Balance <= 0)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["product"] = ["Código, descrição e saldo maior que zero são obrigatórios."] });
    if (!input.Code.Trim().All(char.IsDigit) || !input.Description.Trim().All(character => char.IsLetter(character) || char.IsWhiteSpace(character)))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["product"] = ["O código deve conter apenas números e a descrição apenas letras."] });
    var code = input.Code.Trim();
    var description = input.Description.Trim();
    var existing = await db.Products.SingleOrDefaultAsync(product => product.Code == code);
    if (existing is not null)
    {
        if (!string.Equals(existing.Description, description, StringComparison.OrdinalIgnoreCase))
            return Results.Conflict(new { message = "Já existe um produto com este código e uma descrição diferente." });
        existing.Balance += input.Balance;
        await db.SaveChangesAsync();
        return Results.Ok(new { product = existing, message = "Saldo do produto atualizado." });
    }
    var productWithSameDescription = await db.Products.SingleOrDefaultAsync(product => EF.Functions.Collate(product.Description, "NOCASE") == description);
    if (productWithSameDescription is not null)
        return Results.Conflict(new { message = $"O produto com a descrição '{productWithSameDescription.Description}' já está cadastrado com o código {productWithSameDescription.Code}." });
    var product = new Product { Code = code, Description = description, Balance = input.Balance };
    db.Products.Add(product);
    await db.SaveChangesAsync();
    return Results.Created($"/api/products/{product.Id}", product);
});
app.MapPut("/api/products/{id:int}", async (int id, CreateProduct input, StockDb db) =>
{
    var product = await db.Products.FindAsync(id);
    if (product is null) return Results.NotFound();
    if (input.Balance < 0 || string.IsNullOrWhiteSpace(input.Code) || string.IsNullOrWhiteSpace(input.Description) || !input.Code.Trim().All(char.IsDigit) || !input.Description.Trim().All(character => char.IsLetter(character) || char.IsWhiteSpace(character))) return Results.BadRequest(new { message = "Dados inválidos. O código deve conter apenas números e a descrição apenas letras." });
    product.Code = input.Code.Trim();
    product.Description = input.Description.Trim();
    product.Balance = input.Balance;
    await db.SaveChangesAsync();
    return Results.Ok(product);
});
app.MapPost("/api/stock/consume", async (ConsumeStock input, InventoryService inventory) =>
{
    var result = await inventory.ConsumeAsync(input);
    return result.Success ? Results.Ok(new { message = result.Message }) : Results.Conflict(new { message = result.Message });
});
app.Run();
