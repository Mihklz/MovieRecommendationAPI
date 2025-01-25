using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Security.Claims; 
using System.Threading.Tasks;
using MovieRecommendationAPI.Models;
using MovieRecommendationAPI.Data;
using System;

namespace MovieRecommendationAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ReviewsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ILogger<ReviewsController> _logger;

        public ReviewsController(AppDbContext context, ILogger<ReviewsController> logger)
        {
            _context = context;
            _logger = logger;
        }
        
        // Добавить отзыв
        [HttpPost]
        public async Task<IActionResult> AddReview([FromBody] Review review)
        {
            if (review == null)
            {
                _logger.LogWarning("Попытка добавить пустой объект отзыва.");
                return BadRequest(new { Message = "Review cannot be null." });
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

                // Устанавливаем текущего пользователя
                review.UserId = userId;

                // Проверяем, существует ли фильм
                var movieExists = await _context.Movies.AnyAsync(m => m.Id == review.MovieId);
                if (!movieExists)
                {
                    _logger.LogWarning("Фильм с ID {MovieId} не найден.", review.MovieId);
                    return NotFound(new { Message = $"Movie with Id = {review.MovieId} not found." });
                }

                // Проверка оценки
                if (review.Rating < 1 || review.Rating > 10)
                {
                    _logger.LogWarning("Некорректная оценка: {Rating}.", review.Rating);
                    return BadRequest(new { Message = "Rating must be between 1 and 10." });
                }

                // Добавляем отзыв
                _context.Reviews.Add(review);
                await _context.SaveChangesAsync();

                // Подгружаем фильм и пользователя, чтобы вернуть их имена
                var savedReview = await _context.Reviews
                    .Include(r => r.Movie)
                    .Include(r => r.User)
                    .FirstOrDefaultAsync(r => r.Id == review.Id);

                if (savedReview?.Movie == null || savedReview?.User == null)
                {
                    _logger.LogWarning("Ошибка при подгрузке данных о фильме или пользователе.");
                    return StatusCode(500, new { Message = "Error loading related movie or user data." });
                }

                // Возвращаем отзыв с фильмом и пользователем
                _logger.LogInformation("Добавлен новый отзыв с ID {Id} для фильма {MovieId} пользователем {UserId}.", review.Id, review.MovieId, userId);
                
                var response = new 
                {
                    savedReview.Id,
                    savedReview.Rating,
                    savedReview.Comment,
                    savedReview.CreatedAt,
                    MovieTitle = savedReview.Movie?.Title ?? "Unknown",  // Добавляем название фильма (если есть)
                    MovieGenre = savedReview.Movie?.Genre ?? "Unknown",  // Добавляем жанр фильма (если есть)
                    UserName = savedReview.User?.Username ?? "Unknown"  // Добавляем имя пользователя (если есть)
                };

                return CreatedAtAction(nameof(GetReviewById), new { id = review.Id }, response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при добавлении отзыва.");
                return StatusCode(500, new { Message = "An error occurred while processing your request." });
            }
        }


        
        // Получение всех отзывов для фильма
        [HttpGet("movie/{movieId}")]
        public async Task<IActionResult> GetReviewsByMovieId(int movieId)
        {
            try
            {
                var reviews = await _context.Reviews
                    .Include(r => r.User) // Включаем информацию о пользователе
                    .Include(r => r.Movie) // Включаем информацию о фильме
                    .Where(r => r.MovieId == movieId)
                    .ToListAsync();

                if (reviews == null || !reviews.Any())
                {
                    _logger.LogWarning("Отзывы для фильма с ID {MovieId} не найдены.", movieId);
                    return NotFound(new { Message = "Отзывы для этого фильма отсутствуют." });
                }

                // Если объекты User или Movie отсутствуют, можно их безопасно обработать:
                foreach (var review in reviews)
                {
                    if (review.User == null)
                    {
                        review.User = new User { Username = "Неизвестный пользователь" };
                    }
                    if (review.Movie == null)
                    {
                        review.Movie = new Movie { Title = "Неизвестный фильм", Genre = "Неизвестно" };
                    }
                }

                _logger.LogInformation("Найдено {Count} отзывов для фильма с ID {MovieId}.", reviews.Count, movieId);
                return Ok(reviews);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении отзывов для фильма с ID {MovieId}.", movieId);
                return StatusCode(500, new { Message = "Произошла ошибка при обработке запроса." });
            }
        }
        
        // Получить отзыв по ID
        [HttpGet("{id}")]
        public async Task<IActionResult> GetReviewById(int id)
        {
            try
            {
                var review = await _context.Reviews
                    .Include(r => r.Movie)
                    .Include(r => r.User)
                    .FirstOrDefaultAsync(r => r.Id == id);

                if (review == null)
                {
                    _logger.LogWarning("Отзыв с ID {Id} не найден.", id);
                    return NotFound("Review not found.");
                }

                return Ok(review);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении отзыва с ID {Id}.", id);
                return StatusCode(500, "Произошла ошибка при обработке запроса.");
            }
        }
        

        // Обновить отзыв
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateReview(int id, [FromBody] Review updatedReview)
        {
            if (updatedReview == null)
            {
                _logger.LogWarning("Попытка обновить отзыв с ID {Id} пустыми данными.", id);
                return BadRequest("Review data is required.");
            }

            if (updatedReview.Rating < 1 || updatedReview.Rating > 10)
            {
                _logger.LogWarning("Оценка обновляемого отзыва ({Rating}) вне допустимого диапазона 1-10.", updatedReview.Rating);
                return BadRequest("Rating must be between 1 and 10.");
            }

            // Извлечение ID пользователя из токена
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
            {
                _logger.LogWarning("ID пользователя отсутствует или некорректен в токене.");
                return Unauthorized("Invalid or missing user ID.");
            }

            try
            {
                var review = await _context.Reviews.FindAsync(id);
                if (review == null)
                {
                    _logger.LogWarning("Отзыв с ID {Id} не найден.", id);
                    return NotFound("Review not found.");
                }

                // Проверяем, является ли текущий пользователь владельцем отзыва
                if (review.UserId != userId)
                {
                    _logger.LogWarning("Попытка обновления отзыва с ID {Id} пользователем с ID {UserId}.", id, userId);
                    return Forbid("You can only update your own reviews.");
                }

                // Обновляем данные отзыва
                review.Rating = updatedReview.Rating;
                review.Comment = updatedReview.Comment;
                review.CreatedAt = DateTime.UtcNow; // Обновляем дату

                await _context.SaveChangesAsync();

                _logger.LogInformation("Отзыв с ID {Id} успешно обновлен.", id);
                return Ok(review);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обновлении отзыва с ID {Id}.", id);
                return StatusCode(500, "Произошла ошибка при обработке запроса.");
            }
        }

        

        // Удалить отзыв
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteReview(int id)
        {
            // Извлечение ID пользователя из токена
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
            {
                _logger.LogWarning("ID пользователя отсутствует или некорректен в токене.");
                return Unauthorized("Invalid or missing user ID.");
            }

            try
            {
                var review = await _context.Reviews.FindAsync(id);
                if (review == null)
                {
                    _logger.LogWarning("Отзыв с ID {Id} не найден.", id);
                    return NotFound("Review not found.");
                }

                // Проверяем, является ли текущий пользователь владельцем отзыва
                if (review.UserId != userId)
                {
                    _logger.LogWarning("Попытка удаления отзыва с ID {Id} пользователем с ID {UserId}.", id, userId);
                    return Forbid("You can only delete your own reviews.");
                }

                // Удаляем отзыв
                _context.Reviews.Remove(review);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Отзыв с ID {Id} успешно удален.", id);
                return Ok("Review deleted.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при удалении отзыва с ID {Id}.", id);
                return StatusCode(500, "Произошла ошибка при обработке запроса.");
            }
        }

    }
}