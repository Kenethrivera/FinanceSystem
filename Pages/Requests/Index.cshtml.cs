using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using FinanceSystem.Models;
using FinanceSystem.Services;

namespace FinanceSystem.Pages.Requests
{
    public class IndexModel : PageModel
    {
        private readonly SupabaseService _supabase;
        public IndexModel(SupabaseService supabase) { _supabase = supabase; }

        // List for the Table
        public List<FinancialRequest> MyRequests { get; set; } = new();

        // Bind Property for Create/Edit
        [BindProperty]
        public FinancialRequest RequestInput { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            if (!User.IsInRole("Pastor") && !User.IsInRole("Leader")) return RedirectToPage("/Index");

            await _supabase.InitializeAsync(true);
            string myName = User.FindFirst("FullName")?.Value;

            // Fetch My Requests
            var response = await _supabase.Client.From<FinancialRequest>()
                .Where(r => r.RequestorName == myName)
                .Order("created_at", Supabase.Postgrest.Constants.Ordering.Descending)
                .Get();

            MyRequests = response.Models;
            return Page();
        }

        public async Task<IActionResult> OnPostCreateAsync()
        {
            await _supabase.InitializeAsync(true);

            var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            var allowedRoles = new[] { "Pastor", "Leader" };

            if (!allowedRoles.Contains(role))
            {
                return Forbid();
            }

            RequestInput.RequestorName = User.FindFirst("FullName")?.Value ?? "Unknown";
            RequestInput.Status = "Pending";
            RequestInput.CreatedAt = DateTime.Now;

            if (RequestInput.DateNeeded.HasValue)
            {
                RequestInput.DateNeeded = RequestInput.DateNeeded.Value.Date.AddHours(12);
            }

            await _supabase.Client.From<FinancialRequest>().Insert(RequestInput);

            return RedirectToPage("/Requests/Index");
        }

        public async Task<IActionResult> OnPostUpdateAsync()
        {
            await _supabase.InitializeAsync(true);
            var existing = await _supabase.Client.From<FinancialRequest>().Where(x => x.Id == RequestInput.Id).Single();

            if (existing != null && existing.Status == "Pending")
            {
                existing.Title = RequestInput.Title;
                existing.Amount = RequestInput.Amount;
                existing.Reason = RequestInput.Reason;

                // DATE FIX: Add 12 hours buffer here too
                if (RequestInput.DateNeeded.HasValue)
                {
                    existing.DateNeeded = RequestInput.DateNeeded.Value.Date.AddHours(12);
                }

                await _supabase.Client.From<FinancialRequest>().Update(existing);
            }
            return RedirectToPage("/Requests/Index");
        }

        public async Task<IActionResult> OnPostCancelAsync(long id)
        {
            await _supabase.InitializeAsync(true);

            var existing = await _supabase.Client.From<FinancialRequest>()
                .Where(x => x.Id == id)
                .Single();

            // Only allow delete if Pending
            if (existing != null && existing.Status == "Pending")
            {
                await _supabase.Client.From<FinancialRequest>().Where(x => x.Id == id).Delete();
            }

            return RedirectToPage("/Requests/Index");
        }
    }
}