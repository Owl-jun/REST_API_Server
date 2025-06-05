using BCrypt.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MySqlConnector;
using REST_API;
using REST_API.DBModel;
using REST_API.DTO;
using REST_API.Helpers;
using REST_API.LocalCache;
using StackExchange.Redis;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
// ---------------------------------------------------------
// ȯ�漳��
var builder = WebApplication.CreateBuilder(args);
#region REDIS
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var configuration = ConfigurationOptions.Parse("localhost:6379", true);
    configuration.ResolveDns = true;
    return ConnectionMultiplexer.Connect(configuration);
});
builder.Services.AddSingleton<RedisService>();

#endregion
#region JWT ����
builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options =>
    {
        var config = builder.Configuration;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = config["Jwt:Issuer"],
            ValidAudience = config["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(config["Jwt:Key"]!))
        };
    });

builder.Services.AddAuthorization();

#endregion
#region ����� DB���� �ɼ�
builder.Services.AddDbContext<Dbconn>(options =>
    options.UseMySql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        ServerVersion.AutoDetect(builder.Configuration.GetConnectionString("DefaultConnection"))
    ));

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
});
#endregion
#region Swagger �ɼ�
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT ��ū�� �Ʒ� �Է�â�� �Է��ϼ���. (��: Bearer eyJhbGciOiJIUzI1...)"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseSwagger();
app.UseSwaggerUI();
#endregion
// ---------------------------------------------------------
// ---------------------------------------------------------
// API ������
#region TEST
app.MapGet("/", async (RedisService _redis) =>
{
    await _redis.DeleteAsync($"user:token:admin");
    return Results.Ok(" GO TO /Swagger");
})
.WithSummary("���� ������ ���� �׽�Ʈ�� API");
#endregion

#region UserAPI
var USER = app.MapGroup("/user");

USER.MapPost("/register", async (User user, Dbconn db) =>
{
    try
    {
        // ��й�ȣ �ؽ�
        string hashed = BCrypt.Net.BCrypt.HashPassword(user.Password);
        user.Password = hashed;

        // DB ����
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return Results.Ok("ȸ������ �Ϸ�");
    }
    catch (DbUpdateException ex) when (ex.InnerException is MySqlException mysqlEx &&
                                   mysqlEx.Number == 1062) // Duplicate entry
    {
        return Results.Conflict("�̹� �����ϴ� ����� ID�Դϴ�.");
    }
    catch (DbUpdateException ex)
    {
        return Results.Problem("���� ����" + ex.Message, statusCode: 500);
    }
    catch (Exception ex)
    {
        return Results.Problem("���� ����", statusCode: 500);
    }
})
.WithName("RegisterUser")
.WithTags("Users")
.WithSummary("ȸ�����Կ�û API")
.WithDescription("�ߺ� ���� üũ �� ���� ���θ� ��ȯ�մϴ�.");

// LOGIN
USER.MapPost("/login", async (LoginRequest req, Dbconn db, IConfiguration config, RedisService _redis) =>
{
    try
    {
        // Check Redis Cache
        if (await _redis.ExistsAsync($"user:state:{req.Username}"))
            return Results.Problem("�̹� ���� ���� �����Դϴ�.", statusCode: 401);

        var user = await db.Users.Include(u => u.Inventory)
                                 .ThenInclude(inv => inv.Item)
                                 .FirstOrDefaultAsync(u => u.Username == req.Username);
        // DB Query
        if (user is null)
            return Results.Problem("�������� �ʴ� ������Դϴ�.", statusCode: 401);

        // ��й�ȣ ����
        bool isValid = BCrypt.Net.BCrypt.Verify(req.Password, user.Password);
        if (!isValid)
            return Results.Problem("��й�ȣ�� ��ġ���� �ʽ��ϴ�.", statusCode: 401);

        // JWT ��ū �߱�
        string token = JwtHelper.GenerateJwtToken(user, config);

        // REDIS ĳ��
        var RedisValue = new UserStateDTO { Token = token, UserId = user.Id, UserName = user.Username };
        string value = JsonSerializer.Serialize(RedisValue);
        await _redis.SetAsync($"user:state:{user.Username}", value, TimeSpan.FromHours(1));

        // ����ȭ �޽��� ����
        var msg = new MessageLogDTO { oper = 0, UserState = RedisValue };   // 0 : Login , 1 : Logout
        string message = JsonSerializer.Serialize(msg);
        await _redis.Publish("user:state:update", message);

        return Results.Ok(RedisValue);
    }
    catch (Exception ex)
    {
        return Results.Problem("���� ����" + ex.Message, statusCode: 500);
    }
})
.WithName("Login")
.WithTags("Users")
.WithSummary("�α��� ��û API")
.WithDescription("���н� ���� ����, ��й�ȣ ����ġ, �ߺ� �α��� ����, ���� ���� �����Ͽ� �����մϴ�.");

// LOGOUT
USER.MapPost("/logout", async (HttpContext ctx, RedisService _redis) =>
{
    var auth = ctx.Request.Headers["Authorization"].ToString();
    if (string.IsNullOrEmpty(auth) || !auth.StartsWith("Bearer "))
        return Results.Problem("��ȿ���� ���� ��ū �����Դϴ�.", statusCode: 401);

    var token = auth["Bearer ".Length..].Trim();
    var username = JwtHelper.ExtractUsernameFromJwt(token);
    if (username == null)
        return Results.Problem("��ū �Ľ� ����", statusCode: 401);

    var cached = await _redis.GetAsync($"user:token:{username}");
    if (cached != token)
        return Results.Problem("�̹� ����Ǿ��ų� ��ȿ���� ���� ���� �Դϴ�.", statusCode: 401);

    await _redis.DeleteAsync($"user:state:{username}");

    // ����ȭ �޽��� ����
    var msg = new MessageLogDTO { oper = 1, UserState = new UserStateDTO { UserName = username } };   // 0 : Login , 1 : Logout
    string message = JsonSerializer.Serialize(msg);
    await _redis.Publish("user:state:update", message);

    return Results.Ok("�α׾ƿ� ����");
})
.WithName("LOGOUT")
.WithTags("Users")
.WithSummary("�α׾ƿ� ��û API")
.WithDescription("Ŭ���̾�Ʈ ����� �ʼ������� ȣ���ؾ��ϴ� API �Դϴ�. (Ŭ���̾�Ʈ ����ȭ)");
#endregion

app.Run();


// ---------------------------------------------------------
// DTO
record LoginRequest(string Username, string Password);
// ---------------------------------------------------------
