using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using REST_API.Entity;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using System.Text;
using REST_API.Common;
using REST_API.DTO;
using MySqlConnector;
using Microsoft.AspNetCore.Mvc;

namespace REST_API.UserService
{
    [ApiController]
    [Route("api/[controller]")]
    public class UserController : ControllerBase
    {
        private readonly Dbconn _db;
        private readonly RedisService _redis;
        private readonly IConfiguration _config;

        public UserController(Dbconn db, RedisService redis, IConfiguration config)
        {
            _db = db;
            _redis = redis;
            _config = config;
        }

        [ProducesResponseType(typeof(string), 200)]
        [ProducesResponseType(typeof(ProblemDetails),400)]
        [ProducesResponseType(typeof(ProblemDetails),409)]
        [ProducesResponseType(typeof(ProblemDetails),500)]
        [HttpPost("reg")]
        public async Task<IActionResult> RegisterAsync(User user)
        {
            try
            {
                string hashed = BCrypt.Net.BCrypt.HashPassword(user.Password);
                user.Password = hashed;

                // DB 저장
                _db.Users.Add(user);
                await _db.SaveChangesAsync();

                return Ok("회원가입 완료");
            }
            catch (DbUpdateException ex) when (ex.InnerException is MySqlException mysqlEx &&
                                           mysqlEx.Number == 1062) // Duplicate entry
            {
                return Problem("이미 존재하는 사용자 ID입니다.",statusCode:409);
            }
            catch (DbUpdateException ex)
            {
                return Problem("가입 실패" + ex.Message, statusCode: 400);
            }
            catch (Exception ex)
            {
                return Problem("서버 오류", statusCode: 500);
            }
        }

        [ProducesResponseType(typeof(UserStateDTO), 200)]
        [ProducesResponseType(typeof(ProblemDetails),400)]
        [ProducesResponseType(typeof(ProblemDetails),409)]
        [HttpPost("login")]
        public async Task<ActionResult<UserStateDTO>> LoginAsync(LoginRequestDTO req)
        {
            if (await _redis.ExistsAsync($"user:state:{req.Username}"))
                return Problem("이미 접속 중인 유저입니다.", statusCode:409 );

            var user = await _db.Users.Include(u => u.Inventory)
                                      .ThenInclude(inv => inv.Item)
                                      .FirstOrDefaultAsync(u => u.Username == req.Username);

            if (user == null)
                return NotFound();

            if (!BCrypt.Net.BCrypt.Verify(req.Password, user.Password))
                return Problem("비밀번호가 일치하지 않습니다.", statusCode:400);

            string token = JwtHelper.GenerateJwtToken(user, _config);

            var value = new UserStateDTO { Token = token, UserId = user.Id, UserName = user.Username };
            await _redis.SetAsync($"user:state:{user.Username}", JsonSerializer.Serialize(value), TimeSpan.FromHours(1));

            // 동기화 메시지 전송
            var msg = new MessageLogDTO { oper = 0, UserState = value };   // 0 : Login , 1 : Logout
            await _redis.Publish("user:state:update", JsonSerializer.Serialize(msg));

            return Ok(value);
        }

        [ProducesResponseType(typeof(string), 200)]
        [ProducesResponseType(typeof(ProblemDetails),401)]
        [ProducesResponseType(typeof(ProblemDetails),404)]
        [HttpPost("logout")]
        public async Task<IActionResult> LogoutAsync()
        {
            var auth = HttpContext.Request.Headers["Authorization"].ToString();
            var token = auth["Bearer ".Length..].Trim();
            if (string.IsNullOrEmpty(auth) || !auth.StartsWith("Bearer "))
                return Problem("유효하지 않은 토큰 형식입니다.",statusCode:401);

            var username = JwtHelper.ExtractUsernameFromJwt(token);
            if (username == null)
                return Problem("잘못된 토큰입니다.",statusCode:401);

            var cached = await _redis.GetAsync($"user:state:{username}");
            var dto = JsonSerializer.Deserialize<UserStateDTO>(cached);
            if (dto == null || dto.Token != token)
                return Problem("이미 만료되었거나 유효하지 않은 세션 입니다.",statusCode:404);

            await _redis.DeleteAsync($"user:state:{username}");

            // 동기화 메시지 전송
            var msg = new MessageLogDTO { oper = 1, UserState = new UserStateDTO { UserName = username } };   // 0 : Login , 1 : Logout
            string message = JsonSerializer.Serialize(msg);
            await _redis.Publish("user:state:update", message);

            return Ok("로그아웃 완료");
        }

    }
}

