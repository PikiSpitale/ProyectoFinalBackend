using System.ComponentModel.DataAnnotations;

namespace ProyectAPI.DTOs
{
    public class UpdateTeamDto
    {
        [StringLength(40, MinimumLength = 2)]
        public string? Name { get; set; }

        [StringLength(6, MinimumLength = 2)]
        public string? Tag { get; set; }

        public string? LogoUrl { get; set; }

        // "InviteOnly" | "Open"
        public string? JoinPolicy { get; set; }
        // "None" | "Fixed" | "Minimum"
        public string? RankFilterType { get; set; }
        public string? RankFilterValue { get; set; }
    }
}