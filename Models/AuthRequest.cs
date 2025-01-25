namespace MovieRecommendationAPI.Models
{
    public class AuthRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;

        // Добавьте свойство Role
        public string Role { get; set; } = "User"; // По умолчанию роль "User"
    }
}





