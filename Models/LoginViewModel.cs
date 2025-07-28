using System.ComponentModel.DataAnnotations;

namespace alloy_events_test.Models;

public class LoginViewModel
{
    [Required]
    public string Username { get; set; }

    [Required]
    public string Password { get; set; }
}
