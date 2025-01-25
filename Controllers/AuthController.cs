using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BCrypt.Net;
using Microsoft.EntityFrameworkCore;
using MovieRecommendationAPI.Models;
using MovieRecommendationAPI.Data;

namespace MovieRecommendationAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthController> _logger;

        public AuthController(AppDbContext context, IConfiguration configuration, ILogger<AuthController> logger)
        {
            _context = context;
            _configuration = configuration;
            _logger = logger;
        }

        // Регистрация нового пользователя
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] AuthRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.Username) || string.IsNullOrEmpty(request.Password))
            {
                _logger.LogWarning("Регистрация не выполнена: пустое имя пользователя или пароль.");
                return BadRequest("Имя пользователя и пароль не могут быть пустыми.");
            }

            if (await _context.Users.AnyAsync(u => u.Username == request.Username))
            {
                _logger.LogWarning("Регистрация не выполнена: пользователь с именем {Username} уже существует.", request.Username);
                return BadRequest("Пользователь с таким именем уже существует.");
            }

            // Назначаем роль по умолчанию или используем переданную роль
            var userRole = string.IsNullOrEmpty(request.Role) ? "User" : request.Role;

            var user = new User
            {
                Username = request.Username,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password), // Хэширование пароля
                Role = userRole // Устанавливаем роль
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Пользователь {Username} успешно зарегистрирован.", request.Username);
            return Ok("Регистрация прошла успешно.");
        }


        // Авторизация пользователя
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] AuthRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.Username) || string.IsNullOrEmpty(request.Password))
            {
                _logger.LogWarning("Авторизация не выполнена: пустое имя пользователя или пароль.");
                return BadRequest("Имя пользователя и пароль не могут быть пустыми.");
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == request.Username);
            if (user == null)
            {
                _logger.LogWarning("Авторизация не выполнена: пользователь с именем {Username} не найден.", request.Username);
                return Unauthorized("Неверные учетные данные.");
            }

            if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            {
                _logger.LogWarning("Авторизация не выполнена: неверный пароль для пользователя {Username}.", request.Username);
                return Unauthorized("Неверные учетные данные.");
            }

            var token = GenerateJwtToken(user);

            _logger.LogInformation("Пользователь {Username} успешно авторизовался.", request.Username);
            return Ok(new { Token = token });
        }

        // Генерация JWT токена
        private string GenerateJwtToken(User user)
        {
            var keyString = _configuration["Jwt:Key"];
            if (string.IsNullOrEmpty(keyString))
            {
                _logger.LogError("JWT ключ не задан в конфигурации.");
                throw new ArgumentNullException(nameof(keyString), "JWT ключ не может быть null или пустым.");
            }

            // Проверка длины ключа
            var keyBytes = Encoding.UTF8.GetBytes(keyString);
            if (keyBytes.Length < 32) // 256 бит = 32 байта
            {
                _logger.LogError("Длина JWT ключа недостаточна. Требуется не менее 256 бит (32 байта). Текущая длина: {KeyLength} байт.", keyBytes.Length);
                throw new ArgumentException("JWT ключ должен быть не менее 256 бит (32 байта).");
            }

            var creds = new SigningCredentials(new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Role, user.Role) // Роль пользователя
            };

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                expires: DateTime.Now.AddHours(1),
                signingCredentials: creds
            );

            _logger.LogInformation("JWT токен успешно сгенерирован для пользователя: {Username}", user.Username);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

    }
}


