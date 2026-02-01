using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using FinanceSystem.Services;
using FinanceSystem.Models;

namespace FinanceSystem.Pages.History
{
    public class IndexModel : PageModel
    {
        private readonly SupabaseService _supabase;
        public IndexModel(SupabaseService supabase) { _supabase = supabase; }

        public List<Financial_Day> Days { get; set; } = new();

        [BindProperty(SupportsGet = true)]
        public string SearchTerm { get; set; } // The text from the search bar

        [BindProperty(SupportsGet = true)]
        public int CurrentPage { get; set; } = 1;

        public int TotalPages { get; set; }
        public int PageSize { get; set; } = 10; // Show 10 days per page

        public async Task OnGetAsync()
        {
            await _supabase.InitializeAsync(true);

            // Start the query
            var query = _supabase.Client.From<Financial_Day>()
                .Order("activity_date", Supabase.Postgrest.Constants.Ordering.Descending);

            // Simple Search: Filter by Notes (You can expand this later)
            if (!string.IsNullOrEmpty(SearchTerm))
            {
                // Note: Supabase search syntax uses "ilike" for case-insensitive search
                query = query.Filter("notes", Supabase.Postgrest.Constants.Operator.ILike, $"%{SearchTerm}%");
            }

            // Pagination Logic
            // We need to fetch a range (e.g., Row 1 to 10, then Row 11 to 20)
            int start = (CurrentPage - 1) * PageSize;
            int end = start + PageSize - 1;

            var response = await query.Range(start, end).Get();
            Days = response.Models;

            
        }
    }
}