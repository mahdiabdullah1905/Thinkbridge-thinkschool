using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrderApi.Data;
using OrderApi.Repositories;
using OrderApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.SuppressModelStateInvalidFilter = true;
    });

// Add Database
builder.Services.AddDbContext<OrderApi.Data.AppDbContext>(options =>
    options.UseInMemoryDatabase("OrderDb"));
    
builder.Services.AddScoped<BadOrderControllerDemo.Controllers.AppDbContext>();

// Register Services and Repositories
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IEmailService, EmailService>();

var app = builder.Build();

app.MapControllers();

app.Run();

// Make the implicit Program class public so test projects can access it
public partial class Program { }
