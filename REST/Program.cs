using BCrypt.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MySqlConnector;
using REST_API.DBModel;
using REST_API.DTO;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// JWT
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

#region 포멜로 DB연동 옵션
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

#region Swagger 옵션
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseSwagger();
app.UseSwaggerUI();
#endregion

// ---------------------------------------------------------------
// ------------ GET ----------------------------------------------
app.MapGet("/inventory", [Authorize] async (ClaimsPrincipal user, Dbconn db) =>
{
    var userId = int.Parse(user.FindFirst("user_id")?.Value ?? "0");
    var inventory = await db.Inventory
        .Include(i => i.Item)
        .Where(i => i.User_Id == userId)
        .ToListAsync();

    return Results.Ok(inventory);
});


// ---------------------------------------------------------------
// ------------ POST ---------------------------------------------

app.MapPost("/register", async (User user, Dbconn db) =>
{
    try
    {
        // 비밀번호 해싱
        string hashed = BCrypt.Net.BCrypt.HashPassword(user.Password);
        user.Password = hashed;

        // DB 저장
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return Results.Ok("회원가입 완료");
    }
    catch (DbUpdateException ex) when (ex.InnerException is MySqlException mysqlEx &&
                                   mysqlEx.Number == 1062) // Duplicate entry
    {
        return Results.Conflict("이미 존재하는 사용자 ID입니다.");
    }
    catch (DbUpdateException ex)
    {
        return Results.Problem("가입 실패" + ex.Message , statusCode: 500);
    }
    catch (Exception ex)
    {
        return Results.Problem("서버 오류" , statusCode: 500);
    }
})
.WithName("RegisterUser")
.WithTags("Users");


app.MapPost("/login", async (LoginRequest req, Dbconn db, IConfiguration config) =>
{
    try
    {
        var user = await db.Users.Include(u => u.Inventory)
                                 .ThenInclude(inv => inv.Item)
                                 .FirstOrDefaultAsync(u => u.Username == req.Username);

        if (user is null)
            return Results.Problem("존재하지 않는 사용자입니다.", statusCode: 401);

        bool isValid = BCrypt.Net.BCrypt.Verify(req.Password, user.Password);
        if (!isValid)
            return Results.Problem("비밀번호가 일치하지 않습니다.", statusCode: 401);

        string token = GenerateJwtToken(user, config);

        var inventoryDtos = user.Inventory.Select(i => new InventoryDTO
        {
            InventoryId = i.Inventory_Id,
            Quantity = i.Quantity,
            AcquiredAt = i.acquired_at,
            Item = new ItemDTO
            {
                Name = i.Item?.Name ?? "Unknown",
                Description = i.Item?.Description ?? "",
                Type = i.Item?.Type ?? "",
                Rarity = i.Item?.Rarity ?? ""
            }
        }).ToList();

        var userDto = new UserDTO
        {
            Id = user.Id,
            Name = user.Username,
            Inventory = inventoryDtos
        };

        return Results.Ok(new
        {
            message = "로그인 성공",
            token = token,
            user = userDto
        });
    }
    catch (Exception ex)
    {
        return Results.Problem("서버 오류" + ex.Message, statusCode: 500);
    }
})
    .WithName("Login")
    .WithTags("Users");
// ---------------------------------------------------------------

app.Run();

// class
string GenerateJwtToken(User user, IConfiguration config)
{
    var claims = new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, user.Username),
        new Claim("user_id", user.Id.ToString()),
        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };
    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

    var token = new JwtSecurityToken(
        issuer: config["Jwt:Issuer"],
        audience: config["Jwt:Audience"],
        claims: claims,
        expires: DateTime.Now.AddMinutes(double.Parse(config["Jwt:ExpiresInMinutes"]!)),
        signingCredentials: creds
    );

    return new JwtSecurityTokenHandler().WriteToken(token);
};


// DTO
record LoginRequest(string Username, string Password);