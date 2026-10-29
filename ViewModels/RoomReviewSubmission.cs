using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace TroTot.Web.ViewModels;

public class RoomReviewSubmission
{
    [Range(1, 5)]
    public int Rating { get; set; }

    [Required]
    [StringLength(2000, MinimumLength = 1)]
    public string Content { get; set; } = "";

    public IFormFile? Image { get; set; }
}
