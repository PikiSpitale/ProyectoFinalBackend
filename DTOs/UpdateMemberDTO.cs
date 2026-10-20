namespace ProyectAPI.DTOs
{
    public class UpdateMemberRoleDto
    {
        public string Role { get; set; } = "Member";
        public bool IsStarter { get; set; } = true;
    }
}