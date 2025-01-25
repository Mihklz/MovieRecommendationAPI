using Microsoft.EntityFrameworkCore;
using MovieRecommendationAPI.Models;

namespace MovieRecommendationAPI.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Movie> Movies { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Review> Reviews { get; set; } // Добавляем DbSet для отзывов

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Movie>(entity =>
            {
                entity.ToTable("movies");

                entity.HasKey(m => m.Id);

                entity.Property(m => m.Title)
                    .IsRequired()
                    .HasMaxLength(200);

                entity.Property(m => m.Genre)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(m => m.Year)
                    .IsRequired();

                entity.Property(m => m.Rating)
                    .IsRequired();

                entity.Property(m => m.IsPublic)
                    .IsRequired()
                    .HasDefaultValue(false);

                // Настройка связи с таблицей Users
                entity.HasOne(m => m.User) // Указываем связь с навигационным свойством
                    .WithMany() // Один пользователь может иметь много фильмов
                    .HasForeignKey(m => m.UserId)
                    .OnDelete(DeleteBehavior.Cascade); // Удаление всех фильмов при удалении пользователя

                // Настройка связи с таблицей Reviews
                entity.HasMany(m => m.Reviews) // Один фильм может иметь много отзывов
                    .WithOne(r => r.Movie) // Указываем связь с навигационным свойством в Review
                    .HasForeignKey(r => r.MovieId)
                    .OnDelete(DeleteBehavior.Cascade); // Удаление отзывов при удалении фильма
            });
        }
    }
}






