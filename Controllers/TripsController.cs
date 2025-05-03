using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using WebApplication3.Models.DTOs;
using WebApplication3.Models;
using WebApplication3.Exceptions; // Dodaj ten using!

namespace TravelAgencyAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TripsController : ControllerBase
    {
        private readonly IConfiguration _config;

        public TripsController(IConfiguration config)
        {
            _config = config;
        }

        [HttpGet]
        public async Task<IActionResult> GetTrips()
        {
            var trips = new Dictionary<int, TripDTO>();
            
            using var connection = new SqlConnection(_config.GetConnectionString("Default"));
            await connection.OpenAsync();

            var query = @"
                SELECT t.IdTrip, t.Name, t.Description, t.DateFrom, t.DateTo, t.MaxPeople, c.Name AS Country
                FROM Trip t
                JOIN Country_Trip ct ON t.IdTrip = ct.IdTrip
                JOIN Country c ON ct.IdCountry = c.IdCountry
                ORDER BY t.DateFrom DESC";

            using var command = new SqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var tripId = reader.GetInt32(0);
                if (!trips.ContainsKey(tripId))
                {
                    trips[tripId] = new TripDTO
                    {
                        IdTrip = tripId,
                        Name = reader.GetString(1),
                        Description = reader.GetString(2),
                        DateFrom = reader.GetDateTime(3),
                        DateTo = reader.GetDateTime(4),
                        MaxPeople = reader.GetInt32(5),
                        Countries = new List<string>()
                    };
                }
                trips[tripId].Countries.Add(reader.GetString(6));
            }

            if (trips.Count == 0)
                throw new NotFoundException("Nie znaleziono żadnych wycieczek.");

            return Ok(trips.Values);
        }
    }
}
