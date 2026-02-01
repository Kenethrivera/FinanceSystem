using Supabase;
using Supabase.Postgrest;
using Microsoft.Extensions.Configuration; // Ensure this is using correct namespace
using System.Threading.Tasks;
using System;

namespace FinanceSystem.Services
{
    public class SupabaseService
    {
        public Supabase.Client Client { get; private set; } = null!;
        private readonly IConfiguration _configuration;

        public SupabaseService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // CHANGED: Default is now FALSE (Safety First)
        public async Task InitializeAsync(bool useServiceRole = false)
        {
            var url = _configuration["Supabase:Url"]
                ?? throw new Exception("Supabase URL not found");

            // Logic: If useServiceRole is requested, try to get that key.
            // Otherwise, use the standard Anon Key.
            string key;

            if (useServiceRole)
            {
                key = _configuration["Supabase:ServiceRoleKey"];
                if (string.IsNullOrEmpty(key))
                {
                    // Fail safe: If they asked for Admin access but no key is strictly defined,
                    // do not fall back to Anon. Throw an error to alert the dev.
                    throw new Exception("CRITICAL: ServiceRoleKey requested but not found in configuration.");
                }
            }
            else
            {
                // In your appsettings you named it "Key", let's map it here
                key = _configuration["Supabase:Key"];
            }

            if (string.IsNullOrEmpty(key))
                throw new Exception("Supabase Key (Anon) not found");

            var options = new SupabaseOptions
            {
                AutoConnectRealtime = false
            };

            Client = new Supabase.Client(url, key, options);
            await Client.InitializeAsync();
        }

        public async Task LogActvity(string username, string action, string details)
        {
            try
            {
                var log = new FinanceSystem.Models.AuditLog
                {
                    Username = username,
                    Action = action,
                    Details = details,
                    Timestamp = DateTime.Now
                };

                // Audit logs usually require permission to write, 
                // ensure the current Client is initialized with enough permission 
                // OR RLS policies allow authenticated users to INSERT into audit_logs.
                await Client.From<FinanceSystem.Models.AuditLog>().Insert(log);
            }
            catch (Exception ex)
            {
                // Consider logging this to a file or console so you know if auditing fails
                Console.WriteLine($"[SECURITY WARNING] Audit Log Failed: {ex.Message}");
            }
        }
    }
}