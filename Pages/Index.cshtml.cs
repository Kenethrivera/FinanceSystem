using Microsoft.AspNetCore.Mvc.RazorPages;
using FinanceSystem.Services;
using FinanceSystem.Models;
using FinanceSystem.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace FinanceSystem.Pages
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly SupabaseService _supabase;

        public IndexModel(SupabaseService supabase)
        {
            _supabase = supabase;
        }

        public List<FinancialDayViewModel> RecentActivty { get; set; } = new();
        public List<PlannedExpenseViewModel> UpcomingExpenses { get; set; } = new();
        public List<PlannedExpense> AllPlans { get; set; } = new();

        [BindProperty]
        public PlannedExpense NewPlan { get; set; }

        [BindProperty]
        public PlannedExpense EditPlan { get; set; }

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

            // Fetch Data
            var days = await _supabase.Client.From<Financial_Day>().Get();
            var budgets = await _supabase.Client.From<Budget>().Get();
            var expenses = await _supabase.Client.From<Expenses>().Get();
            var sources = await _supabase.Client.From<Source>().Get();
            var categories = await _supabase.Client.From<Category>().Get();
            var allGivings = await _supabase.Client.From<MemberGiving>().Get();

            // 1. CALCULATE GLOBAL TOTALS
            GrandTotal_Pledges = 0;
            GrandTotal_CashOnHand = 0;
            decimal TotalSolomonGlobal = 0;

            var totalSolomon = allGivings.Models.Sum(g => g.Solomon_Amount);
            var totalTithesOff = allGivings.Models.Sum(g => g.Tithes_Amount + g.Offering_Amount);

            TotalSolomonGlobal = totalSolomon;
            GrandTotal_Pledges += totalSolomon;
            GrandTotal_CashOnHand += totalTithesOff;

            foreach (var b in budgets.Models)
            {
                var sName = sources.Models.FirstOrDefault(s => s.Id == b.Source_Id)?.Name?.ToLower() ?? "";
                if (sName.Contains("pledge")) GrandTotal_Pledges += b.Amount;
                else GrandTotal_CashOnHand += b.Amount;
            }

            foreach (var e in expenses.Models)
            {
                var sName = sources.Models.FirstOrDefault(s => s.Id == e.Source_Id)?.Name?.ToLower() ?? "";
                if (sName.Contains("pledge")) GrandTotal_Pledges -= e.Amount;
                else GrandTotal_CashOnHand -= e.Amount;
            }

            ViewData["TotalSolomon"] = TotalSolomonGlobal;

            // 2. UPCOMING EXPENSES LOGIC
            var plansResponse = await _supabase.Client.From<PlannedExpense>()
                .Order("id", Supabase.Postgrest.Constants.Ordering.Ascending)
                .Get();
            AllPlans = plansResponse.Models;

            var currentMonthStart = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var currentMonthEnd = currentMonthStart.AddMonths(1).AddSeconds(-1);

            var thisMonthDayIds = days.Models
                .Where(d => d.Activity_Date >= currentMonthStart && d.Activity_Date <= currentMonthEnd)
                .Select(d => d.Id)
                .ToList();

            var actualExpensesThisMonth = expenses.Models
                .Where(e => thisMonthDayIds.Contains(e.Financial_Day_Id))
                .ToList();

            UpcomingExpenses.Clear();

            foreach (var plan in AllPlans)
            {
                bool isDueThisMonth = false;
                DateTime dueDate = DateTime.Now;

                if (plan.IsMonthly)
                {
                    isDueThisMonth = true;
                    dueDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, plan.DueDay ?? 1);
                }
                else if (plan.TargetDate.HasValue && plan.TargetDate.Value.Month == DateTime.Now.Month && plan.TargetDate.Value.Year == DateTime.Now.Year)
                {
                    isDueThisMonth = true;
                    dueDate = plan.TargetDate.Value;
                }

                if (isDueThisMonth)
                {
                    var matchingExpenses = actualExpensesThisMonth.Where(e =>
                    {
                        bool expenseNoteMatch = (e.Notes ?? "").Contains(plan.Title, StringComparison.OrdinalIgnoreCase);

                        var catName = categories.Models.FirstOrDefault(c => c.Id == e.Category_Id)?.Name ?? "";
                        bool catMatch = catName.Equals(plan.Title, StringComparison.OrdinalIgnoreCase);

                        var parentDay = days.Models.FirstOrDefault(d => d.Id == e.Financial_Day_Id);
                        bool parentNoteMatch = (parentDay?.Notes ?? "").Contains(plan.Title, StringComparison.OrdinalIgnoreCase);

                        return expenseNoteMatch || catMatch || parentNoteMatch;
                    }).ToList();

                    decimal amountPaid = matchingExpenses.Sum(e => e.Amount);
                    bool isPaid = false;

                    if (plan.Amount > 0)
                    {
                        if (amountPaid >= (plan.Amount * 0.85m)) isPaid = true;
                    }
                    else
                    {
                        if (matchingExpenses.Any()) isPaid = true;
                    }

                    if (!isPaid)
                    {
                        UpcomingExpenses.Add(new PlannedExpenseViewModel
                        {
                            Title = plan.Title,
                            Amount = plan.Amount,
                            DueDate = dueDate,
                            IsRecurring = plan.IsMonthly
                        });
                    }
                }
            }
            UpcomingExpenses = UpcomingExpenses.OrderBy(x => x.DueDate).ToList();

            // 3. RECENT ACTIVITY LIST
            IEnumerable<Financial_Day> query = days.Models;

            if (!string.IsNullOrEmpty(SearchText))
            {
                query = query.Where(d => (d.Notes ?? "").Contains(SearchText, StringComparison.OrdinalIgnoreCase));
            }

            if (SearchYear.HasValue) query = query.Where(d => d.Activity_Date.Year == SearchYear.Value);
            else if (SearchMonth.HasValue)
            {
                SearchYear = DateTime.Now.Year;
                query = query.Where(d => d.Activity_Date.Year == SearchYear.Value);
            }

            if (SearchMonth.HasValue) query = query.Where(d => d.Activity_Date.Month == SearchMonth.Value);

            var sortedDays = query.OrderByDescending(d => d.Activity_Date).ToList();
            if (string.IsNullOrEmpty(SearchText) && !SearchMonth.HasValue && !SearchYear.HasValue)
            {
                sortedDays = sortedDays.Take(10).ToList();
            }

            RecentActivty.Clear();

            foreach (var day in sortedDays)
            {
                decimal dayCOH = 0;
                decimal dayPledge = 0;

                var dayGivings = allGivings.Models.Where(g => g.Financial_Day_Id == day.Id);
                dayPledge += dayGivings.Sum(g => g.Solomon_Amount);
                dayCOH += dayGivings.Sum(g => g.Tithes_Amount + g.Offering_Amount);

                var dayBudgets = budgets.Models.Where(b => b.Financial_Day_Id == day.Id);
                foreach (var b in dayBudgets)
                {
                    var sName = sources.Models.FirstOrDefault(s => s.Id == b.Source_Id)?.Name?.ToLower() ?? "";
                    if (sName.Contains("pledge")) dayPledge += b.Amount;
                    else dayCOH += b.Amount;
                }

                var dayExpenses = expenses.Models.Where(e => e.Financial_Day_Id == day.Id);
                foreach (var e in dayExpenses)
                {
                    var sName = sources.Models.FirstOrDefault(s => s.Id == e.Source_Id)?.Name?.ToLower() ?? "";
                    if (sName.Contains("pledge")) dayPledge -= e.Amount;
                    else dayCOH -= e.Amount;
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

            // 4. CHART DATA
            var labels = new List<string>();
            var incomeData = new List<decimal>();
            var expenseData = new List<decimal>();

            for (int i = 5; i >= 0; i--)
            {
                var d = DateTime.Now.AddMonths(-i);
                var start = new DateTime(d.Year, d.Month, 1);
                var end = start.AddMonths(1).AddSeconds(-1);

                labels.Add(d.ToString("MMM"));

                var monthDayIds = days.Models
                    .Where(x => x.Activity_Date >= start && x.Activity_Date <= end)
                    .Select(x => x.Id).ToList();

                decimal inc = 0;
                decimal exp = 0;

                if (monthDayIds.Any())
                {
                    var mBudgets = budgets.Models.Where(x => monthDayIds.Contains(x.Financial_Day_Id)).Sum(x => x.Amount);
                    var mEnvelopes = allGivings.Models.Where(x => monthDayIds.Contains(x.Financial_Day_Id)).Sum(x => x.Total_Amount);

                    inc = mBudgets + mEnvelopes;
                    exp = expenses.Models.Where(x => monthDayIds.Contains(x.Financial_Day_Id)).Sum(x => x.Amount);
                }

                incomeData.Add(inc);
                expenseData.Add(exp);
            }

            ChartLabels = labels.ToArray();
            ChartIncome = incomeData.ToArray();
            ChartExpense = expenseData.ToArray();
        }

        // --- POST HANDLERS ---

        public async Task<IActionResult> OnPostAddPlanAsync()
        {
            await _supabase.InitializeAsync(true);

            if (NewPlan.IsMonthly)
            {
                NewPlan.TargetDate = null;
            }
            else
            {
                NewPlan.DueDay = null;
                // FIX: Add 12 hours to prevent previous-day timezone shift
                if (NewPlan.TargetDate.HasValue)
                {
                    NewPlan.TargetDate = NewPlan.TargetDate.Value.Date.AddHours(12);
                }
            }

            await _supabase.Client.From<PlannedExpense>().Insert(NewPlan);
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostUpdatePlanAsync()
        {
            await _supabase.InitializeAsync(true);

            if (EditPlan.IsMonthly)
            {
                EditPlan.TargetDate = null;
            }
            else
            {
                EditPlan.DueDay = null;
                // FIX: Add 12 hours to prevent previous-day timezone shift
                if (EditPlan.TargetDate.HasValue)
                {
                    EditPlan.TargetDate = EditPlan.TargetDate.Value.Date.AddHours(12);
                }
            }

            await _supabase.Client.From<PlannedExpense>().Update(EditPlan);
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeletePlanAsync(long id)
        {
            await _supabase.InitializeAsync(true);
            await _supabase.Client.From<PlannedExpense>().Where(x => x.Id == id).Delete();
            return RedirectToPage();
        }

        public class PlannedExpenseViewModel
        {
            public string Title { get; set; }
            public decimal Amount { get; set; }
            public DateTime DueDate { get; set; }
            public bool IsRecurring { get; set; }
        }

        public class FinancialDayViewModel
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