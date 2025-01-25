using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MovieRecommendationAPI.Models
{
    public class Movie
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string Genre { get; set; } = string.Empty;

        [Required]
        public int Year { get; set; }

        [Required]
        public double Rating { get; set; }

        [Required]
        public bool IsPublic { get; set; } = true; // Указывает, является ли фильм публичным

        // Добавляем флаг, который будет отвечать за статус топового фильма
        public bool IsTopRated { get; set; } = false; // Стандартно не является топом

        // Связь с пользователем
        public int? UserId { get; set; } // Указывается, если фильм принадлежит конкретному пользователю
        public User? User { get; set; } // Навигационное свойство для связи с пользователем

        // Связь с отзывами
        [JsonIgnore] // Добавляем этот атрибут, чтобы избежать циклической зависимости
        public ICollection<Review> Reviews { get; set; } = new List<Review>(); // Навигационное свойство для отзывов
    }
}










