namespace ProyectAPI.Models
{
    public static class AppRoles
    {
        public const string Moderator = "Moderator";
        public const string Admin = "Admin";

        // Para usar en [Authorize(Roles = AppRoles.Staff)]
        public const string Staff = "Moderator,Admin";
    }
}