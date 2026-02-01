using Microsoft.AspNetCore.Mvc.RazorPages;
using FinanceSystem.Services;
using FinanceSystem.Models;
using FinanceSystem.ViewModels;
using FinanceSystem;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Mvc;

namespace FinanceSystem.Pages
{
    public class IndexModel : PageModel
    {
        private readonly SupabaseService _supabase;

        public IndexModel(SupabaseService supabase)
        {
            _supabase = supabase;
        }
        public List<FinancialDayViewModel> RecentActivty { get; set; } = new();

        // lists for total
        public decimal GrandTotal_CashOnHand { get; set; }
        public decimal GrandTotal_Pledges { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? SearchText { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? SearchMonth { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? SearchYear { get; set; }

        public string[] ChartLabels { get; set; }
        public decimal[] ChartIncome { get; set; }
        public decimal[] ChartExpense { get; set; }

        public async Task OnGetAsync()
        {
            await _supabase.InitializeAsync(true);

            var days = await _supabase.Client.From<Financial_Day>().Get();
            var budgets = await _supabase.Client.From<Budget>().Get();
            var expenses = await _supabase.Client.From<Expenses>().Get();
            var offerings = await _supabase.Client.From<Offering>().Get();
            var sources = await _supabase.Client.From<Source>().Get();

            // Reset Total
            GrandTotal_Pledges = 0;
            GrandTotal_CashOnHand = 0;

            // pledge
            var pledgeIncome = budgets.Models
                .Where(b => sources.Models.FirstOrDefault(s => s.Id == b.Source_Id)?.Name?.ToLower().Contains("pledge") ?? false)
                .Sum(b => b.Amount);

            var pledgeExpenses = expenses.Models
                .Where(e => sources.Models.FirstOrDefault(s => s.Id == e.Source_Id)?.Name?.ToLower().Contains("pledge") ?? false)
                .Sum(e => e.Amount);

            GrandTotal_Pledges = pledgeIncome - pledgeExpenses;

            // coh
            var cohIncome = budgets.Models
                .Where(b => !(sources.Models.FirstOrDefault(s => s.Id == b.Source_Id)?.Name?.ToLower().Contains("pledge") ?? false))
                .Sum(b => b.Amount);

            var offeringIncome = offerings.Models.Sum(o => o.Total);

            // C. Expenses (Paid using COH - Source is Null OR Source is not Pledge)
            var cohExpenses = expenses.Models
                .Where(e => e.Source_Id == null || !(sources.Models.FirstOrDefault(s => s.Id == e.Source_Id)?.Name.ToLower().Contains("pledge") ?? false))
                .Sum(e => e.Amount);

            GrandTotal_CashOnHand = (cohIncome + offeringIncome) - cohExpenses;

            IEnumerable<Financial_Day> query = days.Models;

            if (!string.IsNullOrEmpty(SearchText))
            {
                query = query.Where(d => (d.Notes ?? "").Contains(SearchText, StringComparison.OrdinalIgnoreCase));
            }

            if (SearchYear.HasValue)
            {
                query = query.Where(d => d.Activity_Date.Year == SearchYear.Value);
            }
            else if (SearchMonth.HasValue)
            {
                SearchYear = DateTime.Now.Year;
                query = query.Where(d => d.Activity_Date.Year == SearchYear.Value);
            }

            if (SearchMonth.HasValue)
            {
                query = query.Where(d => d.Activity_Date.Month == SearchMonth.Value);
            }

            bool isSearching = !string.IsNullOrEmpty(SearchText) || SearchMonth.HasValue || SearchYear.HasValue;

            var sortedDays = query.OrderByDescending(d => d.Activity_Date).ToList();

            if (!isSearching)
            {
                sortedDays = sortedDays.Take(10).ToList();
            }

            RecentActivty.Clear();

            var labels = new List<string>();
            var incomeData = new List<decimal>();
            var expenseData = new List<decimal>();

            for (int i = 5; i >= 0; i--)
            {
                var d = DateTime.Now.AddMonths(-i);
                var start = new DateTime(d.Year, d.Month, 1);
                var end = start.AddMonths(1).AddSeconds(-1);

                labels.Add(d.ToString("MMM"));

                var monthDays = (await _supabase.Client.From<Financial_Day>()
                    .Filter("activity_date", Supabase.Postgrest.Constants.Operator.GreaterThanOrEqual, start.ToString("yyyy-MM-dd"))
                    .Filter("activity_date", Supabase.Postgrest.Constants.Operator.LessThanOrEqual, end.ToString("yyyy-MM-dd"))
                    .Get()).Models;

                var monthDayIds = monthDays.Select(x => x.Id).ToList();

                decimal inc = 0;
                decimal exp = 0;

                if (monthDayIds.Any())
                {
                    var bud = (await _supabase.Client.From<Budget>().Get()).Models.Where(x => monthDayIds.Contains(x.Financial_Day_Id)).Sum(x => x.Amount);
                    var off = (await _supabase.Client.From<Offering>().Get()).Models.Where(x => monthDayIds.Contains(x.Financial_Day_Id)).Sum(x => x.Total);
                    var exps = (await _supabase.Client.From<Expenses>().Get()).Models.Where(x => monthDayIds.Contains(x.Financial_Day_Id)).Sum(x => x.Amount);

                    inc = bud + off;
                    exp = exps;
                }

                incomeData.Add(inc);
                expenseData.Add(exp);
            }

            ChartLabels = labels.ToArray();
            ChartIncome = incomeData.ToArray();
            ChartExpense = expenseData.ToArray();

            foreach (var day in sortedDays)
            {
                decimal dayCOH = 0;
                decimal dayPledge = 0;

                var dayBudgets = budgets.Models.Where(b => b.Financial_Day_Id == day.Id);
                var dayOfferings = offerings.Models.Where(o => o.Financial_Day_Id == day.Id);
                var dayExpenses = expenses.Models.Where(e => e.Financial_Day_Id == day.Id);

                foreach (var b in dayBudgets)
                {
                    var sName = sources.Models.FirstOrDefault(s => s.Id == b.Source_Id)?.Name?.ToLower() ?? "";
                    if (sName.Contains("pledge")) dayPledge += b.Amount;
                    else dayCOH += b.Amount;
                    
                }
                dayCOH += dayOfferings.Sum(o => o.Total);

                foreach (var e in dayExpenses)
                {
                    var sName = sources.Models.FirstOrDefault(s => s.Id == e.Source_Id)?.Name?.ToLower() ?? "";

                    if (sName.Contains("pledge"))
                    {
                        dayPledge -= e.Amount; 
                    }
                    else
                    {
                        dayCOH -= e.Amount; 
                    }
                }

                RecentActivty.Add(new FinancialDayViewModel
                {
                    Id = day.Id,
                    Date = day.Activity_Date,
                    IsSunday = day.Is_Sunday,
                    Notes = day.Notes,
                    CashOnHandNet = dayCOH,
                    PledgeAmount = dayPledge
                });
            }
        }

        public  class FinancialDayViewModel
        {
            public long Id { get; set; }
            public DateTime Date { get; set; }
            public bool IsSunday { get; set; }
            public string Notes { get; set; }

            public decimal CashOnHandNet { get; set; }
            public decimal PledgeAmount { get; set; }

        }
    }
}