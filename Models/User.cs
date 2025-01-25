namespace MovieRecommendationAPI.Models
{
    public class User
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        
        // Добавляем свойство для роли пользователя
        public string Role { get; set; } = "User"; // Значение по умолчанию для всех новых пользователей
    }

}



