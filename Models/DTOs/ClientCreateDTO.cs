
using System.ComponentModel.DataAnnotations;

namespace WebApplication3.Models.DTOs;

public class ClientCreateDTO
{
    [Required]
    public string FirstName { get; set; }
        
    [Required]
    public string LastName { get; set; }
        
    [Required]
    [EmailAddress]
    public string Email { get; set; }
        
    public string Telephone { get; set; }
        
    [RegularExpression(@"^\d{11}$")]
    public string Pesel { get; set; }
}