using System.ComponentModel.DataAnnotations;

namespace ProyectAPI.DTOs
{
    public class UpdateTeamDto
    {
        public bool? IsPrivate { get; set; }

        [StringLength(40, MinimumLength = 2)]
        public string? Name { get; set; }

        [StringLength(6, MinimumLength = 2)]
        public string? Tag { get; set; }

        public string? LogoUrl { get; set; }
    }
}