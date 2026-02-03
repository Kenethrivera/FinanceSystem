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

            var daysResponse = await _supabase.Client.From<Financial_Day>()
                .Get();

            var filteredDays = daysResponse.Models
                .Where(d => d.Activity_Date.Month == Month && d.Activity_Date.Year == Year)
                .ToList();

            var filteredDayIds = filteredDays.Select(d => d.Id).ToList();

            var givingResponse = await _supabase.Client.From<MemberGiving>().Get();
            var allGivings = givingResponse.Models
                .Where(g => filteredDayIds.Contains(g.Financial_Day_Id))
                .ToList();

            if (FilterType == "Tithes")
            {
                allGivings = allGivings.Where(g => g.Tithes_Amount > 0).ToList();
            }
            else if (FilterType == "Offering")
            {
                allGivings = allGivings.Where(g => g.Offering_Amount > 0).ToList();
            }
            else if (FilterType == "Solomon")
            {
                allGivings = allGivings.Where(g => g.Solomon_Amount > 0).ToList();
            }

            GroupedHistory = allGivings
                .GroupBy(g => g.Financial_Day_Id)
                .Select(group =>
                {
                    var dayInfo = filteredDays.FirstOrDefault(d => d.Id == group.Key);
                    return new MemberGivingGroup
                    {
                        Date = dayInfo?.Activity_Date ?? DateTime.MinValue,
                        EventName = dayInfo?.Notes ?? "Unknown Date",
                        Givings = group.OrderBy(g => g.Member_Name).ToList()
                    };
                })
                .OrderByDescending(g => g.Date)
                .ToList();
        }
    }
}