using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using FinanceSystem.Services;
using FinanceSystem.Models;

namespace FinanceSystem.Pages.Audit
{
    public class IndexModel : PageModel
    {
        private readonly SupabaseService _supabase;

        public IndexModel(SupabaseService supabase)
        {
            _supabase = supabase;
        }

        public List<AuditLog> Logs { get; set; } = new();

        public Dictionary<string, string> UserNames { get; set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            if (!User.IsInRole("Admin") && !User.IsInRole("Auditor")) return Forbid();

            await _supabase.InitializeAsync(true);

            var users = (await _supabase.Client.From<User>().Get()).Models;

            foreach (var u in users)
            {
                if (!UserNames.ContainsKey(u.Username))
                {
                    UserNames.Add(u.Username, !string.IsNullOrEmpty(u.Full_Name) ? u.Full_Name : u.Username);
                }
            }

            Logs = (await _supabase.Client.From<AuditLog>()
                .Order("timestamp", Supabase.Postgrest.Constants.Ordering.Descending)
                .Limit(100)
                .Get()).Models;

            return Page();
        }
        
    }
}
