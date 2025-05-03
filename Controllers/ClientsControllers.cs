using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using WebApplication3.Models.DTOs;
using WebApplication3.Models;
using WebApplication3.Exceptions;

namespace WebApplication3.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ClientsController : ControllerBase
    {
        private readonly IConfiguration _config;

        public ClientsController(IConfiguration config)
        {
            _config = config;
        }

        [HttpGet("{id}/trips")]
        public async Task<IActionResult> GetClientTrips(int id)
        {
            var trips = new List<ClientTripDTO>();
            
            using var connection = new SqlConnection(_config.GetConnectionString("Default"));
            await connection.OpenAsync();

            var query = @"
                SELECT t.IdTrip, t.Name, t.Description, t.DateFrom, t.DateTo, ct.RegisteredAt, ct.PaymentDate
                FROM Client_Trip ct
                JOIN Trip t ON ct.IdTrip = t.IdTrip
                WHERE ct.IdClient = @ClientId";

            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@ClientId", id);

            using var reader = await command.ExecuteReaderAsync();
            
            while (await reader.ReadAsync())
            {
                trips.Add(new ClientTripDTO
                {
                    IdTrip = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Description = reader.GetString(2),
                    DateFrom = reader.GetDateTime(3),
                    DateTo = reader.GetDateTime(4),
                    RegisteredAt = reader.GetDateTime(5),
                    PaymentDate = reader.IsDBNull(6) ? null : reader.GetDateTime(6)
                });
            }

            if (trips.Count == 0)
                throw new NotFoundException("Nie znaleziono wycieczek dla danego klienta");
                
            return Ok(trips);
        }

        [HttpPost]
        public async Task<IActionResult> CreateClient([FromBody] ClientCreateDTO dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            using var connection = new SqlConnection(_config.GetConnectionString("Default"));
            await connection.OpenAsync();

            var query = @"
                INSERT INTO Client (FirstName, LastName, Email, Telephone, Pesel)
                OUTPUT INSERTED.IdClient
                VALUES (@FirstName, @LastName, @Email, @Telephone, @Pesel)";

            using var command = new SqlCommand(query, connection);
            
            command.Parameters.AddWithValue("@FirstName", dto.FirstName);
            command.Parameters.AddWithValue("@LastName", dto.LastName);
            command.Parameters.AddWithValue("@Email", dto.Email);
            command.Parameters.AddWithValue("@Telephone", dto.Telephone ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@Pesel", dto.Pesel ?? (object)DBNull.Value);

            try
            {
                var newId = (int)await command.ExecuteScalarAsync();
                return CreatedAtAction(nameof(GetClientTrips), new { id = newId }, new { IdClient = newId });
            }
            catch (SqlException ex) when (ex.Number == 2627)
            {
                return Conflict("Klient z podanym PESEL/emailem już istnieje");
            }
        }

        [HttpPut("{id}/trips/{tripId}")]
        public async Task<IActionResult> RegisterForTrip(int id, int tripId)
        {
            using var connection = new SqlConnection(_config.GetConnectionString("Default"));
            await connection.OpenAsync();
            
            using var transaction = connection.BeginTransaction();

            try
            {
                // Sprawdź czy klient istnieje
                var clientCheck = new SqlCommand(
                    "SELECT COUNT(*) FROM Client WHERE IdClient = @Id", 
                    connection, 
                    transaction);
                clientCheck.Parameters.AddWithValue("@Id", id);
                
                if ((int)await clientCheck.ExecuteScalarAsync() == 0)
                    throw new NotFoundException($"Klient o id {id} nie istnieje");

                // Sprawdź dostępność miejsc
                var tripCheck = new SqlCommand(@"
                    SELECT t.MaxPeople, COUNT(ct.IdClient) 
                    FROM Trip t 
                    LEFT JOIN Client_Trip ct ON t.IdTrip = ct.IdTrip 
                    WHERE t.IdTrip = @TripId 
                    GROUP BY t.MaxPeople", 
                    connection, 
                    transaction);
                    
                tripCheck.Parameters.AddWithValue("@TripId", tripId);
                
                using var reader = await tripCheck.ExecuteReaderAsync();
                if (!reader.HasRows) 
                    throw new NotFoundException($"Wycieczka o id {tripId} nie istnieje");
                
                await reader.ReadAsync();
                var maxPeople = reader.GetInt32(0);
                var currentParticipants = reader.GetInt32(1);
                
                if (currentParticipants >= maxPeople)
                    return Conflict("Brak wolnych miejsc");

                // Dodaj rejestrację
                var insertCmd = new SqlCommand(
                    "INSERT INTO Client_Trip (IdClient, IdTrip, RegisteredAt) VALUES (@ClientId, @TripId, GETDATE())", 
                    connection, 
                    transaction);
                    
                insertCmd.Parameters.AddWithValue("@ClientId", id);
                insertCmd.Parameters.AddWithValue("@TripId", tripId);
                await insertCmd.ExecuteNonQueryAsync();

                transaction.Commit();
                return NoContent();
            }
            catch (NotFoundException)
            {
                transaction.Rollback();
                throw;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        [HttpDelete("{id}/trips/{tripId}")]
        public async Task<IActionResult> DeleteRegistration(int id, int tripId)
        {
            using var connection = new SqlConnection(_config.GetConnectionString("Default"));
            await connection.OpenAsync();

            var query = "DELETE FROM Client_Trip WHERE IdClient = @ClientId AND IdTrip = @TripId";
            
            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@ClientId", id);
            command.Parameters.AddWithValue("@TripId", tripId);

            var affectedRows = await command.ExecuteNonQueryAsync();
            
            if (affectedRows == 0)
                throw new NotFoundException($"Nie znaleziono rejestracji klienta {id} na wycieczkę {tripId}");
                
            return NoContent();
        }
    }
}
