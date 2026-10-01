using Klaimin.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<KlaiminDb>((services, options) =>
    options.UseSqlite(services.GetRequiredService<IConfiguration>().GetConnectionString("Klaimin")));
builder.Services.AddIdentity<AppUser, IdentityRole>().AddEntityFrameworkStores<KlaiminDb>();
builder.Services.AddSingleton(services => new ReceiptImages(Path.Combine(
    services.GetRequiredService<IWebHostEnvironment>().ContentRootPath,
    services.GetRequiredService<IConfiguration>()["Storage:ReceiptImages"] ?? "App_Data/receipts")));
builder.Services.AddScoped<ClaimService>();
builder.Services.AddReceiptExtraction(builder.Configuration);

// Every page needs a signed-in user unless it opts out. A filter rather than a fallback policy,
// so the stylesheet stays reachable from the sign-in page.
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AuthorizeFilter());
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    await Seed.RunAsync(
        services.GetRequiredService<KlaiminDb>(),
        services.GetRequiredService<UserManager<AppUser>>(),
        services.GetRequiredService<RoleManager<IdentityRole>>(),
        app.Configuration["Seed:Password"]);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Claims}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
