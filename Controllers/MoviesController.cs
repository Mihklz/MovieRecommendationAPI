using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using MovieRecommendationAPI.Models;
using MovieRecommendationAPI.Data;
using Microsoft.AspNetCore.Authorization;
using System;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using StackExchange.Redis;


namespace MovieRecommendationAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize] // Все методы требуют авторизации, если не указано иное
    public class MoviesController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ILogger<MoviesController> _logger;
        private readonly IDistributedCache _cache;  // Кеширование

        public MoviesController(AppDbContext context, ILogger<MoviesController> logger, IDistributedCache cache)
        {
            _context = context;
            _logger = logger;
            _cache = cache;
        }

        // Получить список публичных фильмов (доступно всем)
        [HttpGet("public")]
        [AllowAnonymous]
        public async Task<IActionResult> GetPublicMovies(
        [FromQuery] string? genre,
        [FromQuery] int? year,
        [FromQuery] double? minRating,
        [FromQuery] double? maxRating,
        [FromQuery] string? sortBy)
        {
            try
            {
                _logger.LogInformation("Получен запрос для публичного списка фильмов.");

                // Создаём уникальный ключ для кеша на основе всех параметров, включая GUID для уникальности
                var cacheKey = $"publicMovies-{genre}-{year}-{minRating}-{maxRating}-{sortBy}";

                // Проверяем, есть ли уже данные в кеше
                var cachedMovies = await _cache.GetStringAsync(cacheKey);
                if (cachedMovies != null)
                {
                    _logger.LogInformation("Данные успешно получены из кеша.");
                    // Десериализуем данные из кеша и возвращаем
                    var moviesFromCache = JsonSerializer.Deserialize<List<Movie>>(cachedMovies);
                    return Ok(moviesFromCache);
                }

                _logger.LogInformation("Данные не найдены в кеше. Загружаем данные из базы данных.");

                // Если данных нет в кеше, выполняем запрос к базе данных
                var query = _context.Movies.Where(m => m.IsPublic);

                // Применяем фильтрацию по параметрам запроса
                if (!string.IsNullOrEmpty(genre))
                {
                    query = query.Where(m => m.Genre.ToLower() == genre.ToLower());
                }

                if (year.HasValue)
                {
                    query = query.Where(m => m.Year == year.Value);
                }

                if (minRating.HasValue)
                {
                    query = query.Where(m => m.Rating >= minRating.Value);
                }

                if (maxRating.HasValue)
                {
                    query = query.Where(m => m.Rating <= maxRating.Value);
                }

                // Сортировка по параметрам
                query = sortBy?.ToLower() switch
                {
                    "title" => query.OrderBy(m => m.Title),
                    "year" => query.OrderBy(m => m.Year),
                    "rating" => query.OrderByDescending(m => m.Rating),
                    _ => query.OrderBy(m => m.Id) // Сортировка по умолчанию
                };

                // Выполняем запрос к базе данных
                var movies = await query.ToListAsync();

                _logger.LogInformation("Данные получены из базы данных.");

                // Сериализуем результат и логируем
                _logger.LogInformation($"Before Serialization: {JsonSerializer.Serialize(movies)}");
                var serializedMovies = JsonSerializer.Serialize(movies);
                _logger.LogInformation($"Serialized movies: {serializedMovies}");

                // Сохраняем сериализованные данные в кеш (срок хранения 5 минут)
                var options = new DistributedCacheEntryOptions()
                    .SetAbsoluteExpiration(TimeSpan.FromMinutes(15));
                await _cache.RemoveAsync(cacheKey); // Удаляем ключ перед добавлением нового
                await _cache.SetStringAsync(cacheKey, serializedMovies, options); // Сохраняем как строку

                _logger.LogInformation("Данные успешно сохранены в кеш.");

                return Ok(movies);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении списка публичных фильмов.");
                return StatusCode(500, "Произошла ошибка при обработке запроса.");
            }
        }

        // Добавить фильм в топ (доступно только для администраторов)
        [HttpPost("top")]
        [Authorize(Roles = "Admin")] // Только для пользователей с ролью "Admin"
        public async Task<IActionResult> AddMovieToTop([FromBody] Movie movie)
        {
            if (movie == null)
            {
                _logger.LogWarning("Попытка добавить пустой объект фильма в топ.");
                return BadRequest(new { Message = "Movie cannot be null." });
            }

            try
            {
                // Устанавливаем фильм как публичный и добавляем в топ
                movie.IsPublic = true;  // Фильм будет публичным
                movie.IsTopRated = true; // Новый флаг для фильмов в топе

                // Валидация входных данных
                if (string.IsNullOrWhiteSpace(movie.Title))
                {
                    _logger.LogWarning("Попытка добавить фильм с отсутствующим заголовком.");
                    return BadRequest(new { Message = "Title is a required field." });
                }

                if (string.IsNullOrWhiteSpace(movie.Genre))
                {
                    _logger.LogWarning("Попытка добавить фильм с отсутствующим жанром.");
                    return BadRequest(new { Message = "Genre is a required field." });
                }

                if (movie.Year <= 1800 || movie.Year > DateTime.Now.Year)
                {
                    _logger.LogWarning("Попытка добавить фильм с некорректным годом выпуска: {Year}.", movie.Year);
                    return BadRequest(new { Message = "Year must be between 1800 and the current year." });
                }

                if (movie.Rating < 0 || movie.Rating > 10)
                {
                    _logger.LogWarning("Попытка добавить фильм с некорректным рейтингом: {Rating}.", movie.Rating);
                    return BadRequest(new { Message = "Rating must be between 0 and 10." });
                }

                // Добавляем фильм в базу данных
                await _context.Movies.AddAsync(movie);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Добавлен новый фильм в топ: {Title} с ID {Id}.", movie.Title, movie.Id);

                // Очищаем кеш для публичных фильмов
                _logger.LogInformation("Очистка кеша для публичных фильмов.");
                await ClearCacheByPatternAsync("publicMovies-*");

                return CreatedAtAction(nameof(GetMovieById), new { id = movie.Id }, movie);

            }
            catch (DbUpdateException dbEx)
            {
                _logger.LogError(dbEx, "Ошибка базы данных при добавлении фильма в топ.");
                return StatusCode(500, new { Message = "A database error occurred while processing your request." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при добавлении фильма в топ.");
                return StatusCode(500, new { Message = "An error occurred while processing your request." });
            }
        }

        // Очистка кеша по шаблону
        private async Task ClearCacheByPatternAsync(string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
            {
                _logger.LogWarning("Шаблон для очистки кеша пуст или null.");
                return;
            }

            if (_cache is IConnectionMultiplexer connection)
            {
                var endpoints = connection.GetEndPoints();
                var server = connection.GetServer(endpoints.First());

                foreach (var key in server.Keys(pattern: pattern))
                {
                    var keyString = key.ToString(); // Преобразуем RedisKey в строку
                    if (!string.IsNullOrEmpty(keyString)) // Проверяем строковое представление ключа
                    {
                        try
                        {
                            await _cache.RemoveAsync(keyString);
                            _logger.LogInformation($"Ключ {keyString} успешно удалён из кеша.");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Ошибка при удалении ключа {keyString} из кеша.");
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Обнаружен пустой или недействительный ключ при очистке кеша.");
                    }
                }
            }
            else
            {
                _logger.LogWarning("Кеш не поддерживает интерфейс IConnectionMultiplexer.");
            }
        }



        // Получить список личных фильмов (доступно только авторизованным пользователям)
        [HttpGet("private")]
        public async Task<IActionResult> GetPrivateMovies()
        {
            try
            {
                var userId = GetCurrentUserId();
                if (userId == null)
                {
                    return Unauthorized("Не удалось определить пользователя.");
                }

                _logger.LogInformation("Запрос личного списка фильмов для пользователя с ID {UserId}.", userId);

                var userMovies = await _context.Movies
                    .Where(m => m.UserId == userId)
                    .ToListAsync();

                return Ok(userMovies);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении личного списка фильмов.");
                return StatusCode(500, "Произошла ошибка при обработке запроса.");
            }
        }

        // Получить фильм по ID
        [HttpGet("{id}")]
        public async Task<IActionResult> GetMovieById(int id)
        {
            try
            {
                var movie = await _context.Movies.FindAsync(id);
                if (movie == null)
                {
                    _logger.LogWarning("Фильм с ID {Id} не найден.", id);
                    return NotFound(new { Message = $"Movie with Id = {id} not found." });
                }

                // Проверка доступа
                var userId = GetCurrentUserId();
                if (!movie.IsPublic && movie.UserId != userId)
                {
                    return Forbid("Доступ к фильму запрещен.");
                }

                _logger.LogInformation("Фильм с ID {Id} успешно найден.", id);
                return Ok(movie);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении фильма с ID {Id}.", id);
                return StatusCode(500, "Произошла ошибка при обработке запроса.");
            }
        }

        [HttpPost]
        public async Task<IActionResult> AddMovie([FromBody] Movie movie)
        {
            if (movie == null)
            {
                _logger.LogWarning("Попытка добавить пустой объект фильма.");
                return BadRequest(new { Message = "Movie cannot be null." });
            }

            try
            {
                // Извлечение ID пользователя из токена
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                {
                    _logger.LogWarning("Не удалось извлечь ID пользователя из токена.");
                    return Unauthorized(new { Message = "User is not authorized." });
                }

                // Устанавливаем значения по умолчанию для приватного фильма
                movie.IsPublic = false; // Приватный фильм
                movie.UserId = userId; // Связываем с текущим пользователем

                // Валидация входных данных
                if (string.IsNullOrWhiteSpace(movie.Title))
                {
                    _logger.LogWarning("Попытка добавить фильм с отсутствующим заголовком.");
                    return BadRequest(new { Message = "Title is a required field." });
                }

                if (string.IsNullOrWhiteSpace(movie.Genre))
                {
                    _logger.LogWarning("Попытка добавить фильм с отсутствующим жанром.");
                    return BadRequest(new { Message = "Genre is a required field." });
                }

                if (movie.Year <= 1800 || movie.Year > DateTime.Now.Year)
                {
                    _logger.LogWarning("Попытка добавить фильм с некорректным годом выпуска: {Year}.", movie.Year);
                    return BadRequest(new { Message = "Year must be between 1800 and the current year." });
                }

                if (movie.Rating < 0 || movie.Rating > 10)
                {
                    _logger.LogWarning("Попытка добавить фильм с некорректным рейтингом: {Rating}.", movie.Rating);
                    return BadRequest(new { Message = "Rating must be between 0 and 10." });
                }

                // Проверка дубликатов фильмов для данного пользователя
                var existingMovie = await _context.Movies
                    .FirstOrDefaultAsync(m => m.Title.ToLower() == movie.Title.ToLower() && m.UserId == userId);

                if (existingMovie != null)
                {
                    _logger.LogWarning("Попытка добавить дубликат фильма: {Title} для пользователя с ID {UserId}.", movie.Title, userId);
                    return Conflict(new { Message = "A movie with the same title already exists for this user." });
                }

                // Добавляем фильм в базу данных
                await _context.Movies.AddAsync(movie);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Добавлен новый фильм: {Title} с ID {Id} для пользователя {UserId}.", movie.Title, movie.Id, userId);
                return CreatedAtAction(nameof(GetMovieById), new { id = movie.Id }, movie);
            }
            catch (DbUpdateException dbEx)
            {
                _logger.LogError(dbEx, "Ошибка базы данных при добавлении фильма.");
                return StatusCode(500, new { Message = "A database error occurred while processing your request." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при добавлении фильма.");
                return StatusCode(500, new { Message = "An error occurred while processing your request." });
            }
        }


        // Обновить существующий фильм
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateMovie(int id, [FromBody] Movie updatedMovie)
        {
            try
            {
                var movie = await _context.Movies.FirstOrDefaultAsync(m => m.Id == id);
                if (movie == null)
                {
                    _logger.LogWarning("Попытка обновления несуществующего фильма с ID {Id}.", id);
                    return NotFound(new { Message = $"Movie with Id = {id} not found." });
                }

                var userId = GetCurrentUserId();
                if (!movie.IsPublic && movie.UserId != userId)
                {
                    return Forbid("Доступ к обновлению фильма запрещен.");
                }

                movie.Title = updatedMovie.Title;
                movie.Genre = updatedMovie.Genre;
                movie.Year = updatedMovie.Year;
                movie.Rating = updatedMovie.Rating;

                _context.Movies.Update(movie);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Обновлён фильм с ID {Id}: {Title}.", movie.Id, movie.Title);
                return Ok(movie);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обновлении фильма с ID {Id}.", id);
                return StatusCode(500, "Произошла ошибка при обработке запроса.");
            }
        }

        // Удалить фильм
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteMovie(int id)
        {
            try
            {
                var movie = await _context.Movies.FirstOrDefaultAsync(m => m.Id == id);
                if (movie == null)
                {
                    _logger.LogWarning("Попытка удаления несуществующего фильма с ID {Id}.", id);
                    return NotFound(new { Message = $"Movie with Id = {id} not found." });
                }

                var userId = GetCurrentUserId();
                if (!movie.IsPublic && movie.UserId != userId)
                {
                    return Forbid("Доступ к удалению фильма запрещен.");
                }

                _context.Movies.Remove(movie);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Удалён фильм с ID {Id}: {Title}.", movie.Id, movie.Title);
                return Ok(new { Message = $"Movie with Id = {id} has been deleted." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при удалении фильма с ID {Id}.", id);
                return StatusCode(500, "Произошла ошибка при обработке запроса.");
            }
        }

        // Вспомогательный метод для получения ID текущего пользователя
        private int? GetCurrentUserId()
        {
            var userIdClaim = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
            return userIdClaim != null ? int.Parse(userIdClaim) : (int?)null;
        }
    }
}
