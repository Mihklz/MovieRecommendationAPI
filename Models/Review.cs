using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MovieRecommendationAPI.Models
{
    public class Review
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [Range(1, 10, ErrorMessage = "Rating must be between 1 and 10.")]
        public int Rating { get; set; }

        [MaxLength(1000)]
        public string? Comment { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Связь с пользователем
        [Required]
        public int UserId { get; set; }
        
        // Убираем цикл сериализации User
        [JsonIgnore]
        public User? User { get; set; }

        // Связь с фильмом
        [Required]
        public int MovieId { get; set; }
        
        // Убираем цикл сериализации Movie
        [JsonIgnore]
        public Movie? Movie { get; set; }
    }
}


