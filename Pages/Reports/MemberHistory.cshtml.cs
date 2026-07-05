using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using FinanceSystem.Models;
using FinanceSystem.Services;

namespace FinanceSystem.Pages.Reports
{
    public class MemberHistoryModel : PageModel
    {
        private readonly SupabaseService _supabase;
        public MemberHistoryModel(SupabaseService supabase) { _supabase = supabase; }

        public List<MemberGivingGroup> GroupedHistory { get; set; } = new();

        [BindProperty(SupportsGet = true)]
        public string FilterType { get; set; } = "All";

        [BindProperty(SupportsGet = true)]
        public int? Month { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? Year { get; set; }

        public async Task OnGetAsync()
        {
            await _supabase.InitializeAsync(true);

            if (!Month.HasValue) Month = DateTime.Now.Month;
            if (!Year.HasValue) Year = DateTime.Now.Year;

            var daysResponse = await _supabase.Client.From<Financial_Day>().Get();
            var givingResponse = await _supabase.Client.From<MemberGiving>().Get();

            var filteredDays = daysResponse.Models
                .Where(d => d.Activity_Date.Month == Month && d.Activity_Date.Year == Year)
                .ToList();

            var filteredDayIds = filteredDays.Select(d => d.Id).ToList();

            // All member giving rows for the selected month
            var monthlyMemberRows = givingResponse.Models
                .Where(g => filteredDayIds.Contains(g.Financial_Day_Id))
                .ToList();

            // Keep only Financial_Day records that are actually related to member giving
            // even if all amounts are zero
            var relevantDayIds = monthlyMemberRows
                .Select(g => g.Financial_Day_Id)
                .Distinct()
                .ToList();

            // Rows to display inside the table
            var displayGivings = monthlyMemberRows;

            if (FilterType == "Tithes")
            {
                displayGivings = displayGivings
                    .Where(g => g.Tithes_Amount > 0)
                    .ToList();
            }
            else if (FilterType == "Offering")
            {
                displayGivings = displayGivings
                    .Where(g => g.Offering_Amount > 0)
                    .ToList();
            }
            else if (FilterType == "Solomon")
            {
                displayGivings = displayGivings
                    .Where(g => g.Solomon_Amount > 0)
                    .ToList();
            }
            else
            {
                // "All" = only show rows with any real giving
                displayGivings = displayGivings
                    .Where(g => (g.Tithes_Amount + g.Offering_Amount + g.Solomon_Amount) > 0)
                    .ToList();
            }

            GroupedHistory = filteredDays
                .Where(d => relevantDayIds.Contains(d.Id))
                .Select(day => new MemberGivingGroup
                {
                    Date = day.Activity_Date,
                    EventName = day.Notes ?? "Unknown Date",
                    Givings = displayGivings
                        .Where(g => g.Financial_Day_Id == day.Id)
                        .OrderBy(g => g.Member_Name)
                        .ToList()
                })
                .OrderByDescending(g => g.Date)
                .ToList();
        }
    }
}