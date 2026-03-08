using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using FinanceSystem.Services;
using FinanceSystem.Models;
using FinanceSystem.ViewModels;

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
        public List<PlannedExpenseViewModel> UpcomingExpenses { get; set; } = new();
        public List<PlannedExpense> AllPlans { get; set; } = new();

        [BindProperty] public PlannedExpense NewPlan { get; set; }
        [BindProperty] public PlannedExpense EditPlan { get; set; }

        public decimal GrandTotal_CashOnHand { get; set; }
        public decimal GrandTotal_Pledges { get; set; }

        [BindProperty(SupportsGet = true)] public string? SearchText { get; set; }
        [BindProperty(SupportsGet = true)] public int? SearchMonth { get; set; }
        [BindProperty(SupportsGet = true)] public int? SearchYear { get; set; }

        public string[] ChartLabels { get; set; }
        public decimal[] ChartIncome { get; set; }
        public decimal[] ChartExpense { get; set; }

        public async Task OnGetAsync()
        {
            await _supabase.InitializeAsync(true);

            var days = (await _supabase.Client.From<Financial_Day>().Get()).Models;
            var budgets = (await _supabase.Client.From<Budget>().Get()).Models;
            var expenses = (await _supabase.Client.From<Expenses>().Get()).Models;
            var sources = (await _supabase.Client.From<Source>().Get()).Models;
            var categories = (await _supabase.Client.From<Category>().Get()).Models;
            var givings = (await _supabase.Client.From<MemberGiving>().Get()).Models;

            var sourceLookup = sources.ToDictionary(x => x.Id, x => x.Name?.ToLower() ?? "");
            var categoryLookup = categories.ToDictionary(x => x.Id, x => x.Name ?? "");

            //--------------------------------------
            // GLOBAL TOTALS
            //--------------------------------------

            GrandTotal_CashOnHand = 0;
            GrandTotal_Pledges = 0;
            decimal totalSolomon = 0;

            var orderedDays = days.OrderBy(x => x.Activity_Date);

            foreach (var day in orderedDays)
            {
                decimal dayCOH = 0;
                decimal dayPledge = 0;

                var dayGivings = givings.Where(x => x.Financial_Day_Id == day.Id);

                dayCOH += dayGivings.Sum(x => x.Tithes_Amount + x.Offering_Amount);
                dayPledge += dayGivings.Sum(x => x.Solomon_Amount);
                totalSolomon += dayGivings.Sum(x => x.Solomon_Amount);

                var dayBudgets = budgets.Where(x => x.Financial_Day_Id == day.Id);

                foreach (var b in dayBudgets)
                {
                    var sName = sourceLookup.ContainsKey(b.Source_Id) ? sourceLookup[b.Source_Id] : "";

                    if (sName == "pledge")
                        dayPledge += b.Amount;
                    else
                        dayCOH += b.Amount;
                }

                var dayExpenses = expenses.Where(x => x.Financial_Day_Id == day.Id);

                foreach (var e in dayExpenses)
                {
                    var sName = e.Source_Id.HasValue && sourceLookup.ContainsKey(e.Source_Id.Value)
    ? sourceLookup[e.Source_Id.Value]
    : "";

                    if (sName == "pledge")
                        dayPledge -= e.Amount;
                    else
                        dayCOH -= e.Amount;
                }

                GrandTotal_CashOnHand += dayCOH;
                GrandTotal_Pledges += dayPledge;
            }

            ViewData["TotalSolomon"] = totalSolomon;

            //--------------------------------------
            // UPCOMING EXPENSES
            //--------------------------------------

            AllPlans = (await _supabase.Client.From<PlannedExpense>().Get()).Models;

            var currentMonth = DateTime.Now.Month;
            var currentYear = DateTime.Now.Year;

            UpcomingExpenses.Clear();

            foreach (var plan in AllPlans)
            {
                bool isDue = false;
                DateTime dueDate = DateTime.Now;

                if (plan.RecurrenceType == "FixedDay")
                {
                    isDue = true;
                    dueDate = new DateTime(currentYear, currentMonth, plan.DueDay ?? 1);
                }
                else if (plan.RecurrenceType == "NthWeekday")
                {
                    if (plan.WeekNumber.HasValue && plan.DayOfWeekNumber.HasValue)
                    {
                        dueDate = GetNthWeekdayOfMonth(
                            currentYear,
                            currentMonth,
                            plan.WeekNumber.Value,
                            (DayOfWeek)plan.DayOfWeekNumber.Value
                        );
                        isDue = true;
                    }
                }
                else if (plan.RecurrenceType == "OneTime"
                         && plan.TargetDate.HasValue
                         && plan.TargetDate.Value.Month == currentMonth
                         && plan.TargetDate.Value.Year == currentYear)
                {
                    isDue = true;
                    dueDate = plan.TargetDate.Value;
                }

                if (!isDue) continue;

                var dueMonthDayIds = days
                    .Where(d => d.Activity_Date.Month == dueDate.Month && d.Activity_Date.Year == dueDate.Year)
                    .Select(d => d.Id)
                    .ToList();

                var matchingExpenses = expenses.Where(e =>
                {
                    var catName = categoryLookup.ContainsKey(e.Category_Id)
                        ? categoryLookup[e.Category_Id]
                        : "";

                    var relatedDay = days.FirstOrDefault(d => d.Id == e.Financial_Day_Id);
                    var dayNote = relatedDay?.Notes ?? "";
                    var expenseNote = e.Notes ?? "";

                    bool sameMonth = dueMonthDayIds.Contains(e.Financial_Day_Id);

                    bool titleMatch =
                        expenseNote.Contains(plan.Title, StringComparison.OrdinalIgnoreCase)
                        || dayNote.Contains(plan.Title, StringComparison.OrdinalIgnoreCase)
                        || catName.Contains(plan.Title, StringComparison.OrdinalIgnoreCase)
                        || plan.Title.Contains(expenseNote, StringComparison.OrdinalIgnoreCase)
                        || plan.Title.Contains(dayNote, StringComparison.OrdinalIgnoreCase)
                        || plan.Title.Contains(catName, StringComparison.OrdinalIgnoreCase);

                    return sameMonth && titleMatch;
                });

                decimal amountPaid = matchingExpenses.Sum(x => x.Amount);

                bool isPaid = plan.Amount > 0
                    ? amountPaid >= plan.Amount * 0.85m
                    : matchingExpenses.Any();

                if (!isPaid)
                {
                    UpcomingExpenses.Add(new PlannedExpenseViewModel
                    {
                        Title = plan.Title,
                        Amount = plan.Amount,
                        DueDate = dueDate,
                        IsRecurring = plan.RecurrenceType == "FixedDay" || plan.RecurrenceType == "NthWeekday"
                    });
                }
            }

            UpcomingExpenses = UpcomingExpenses.OrderBy(x => x.DueDate).ToList();

            //--------------------------------------
            // RECENT ACTIVITY
            //--------------------------------------

            IEnumerable<Financial_Day> query = days;

            if (!string.IsNullOrEmpty(SearchText))
                query = query.Where(x => (x.Notes ?? "").Contains(SearchText, StringComparison.OrdinalIgnoreCase));

            if (SearchYear.HasValue)
                query = query.Where(x => x.Activity_Date.Year == SearchYear);

            if (SearchMonth.HasValue)
                query = query.Where(x => x.Activity_Date.Month == SearchMonth);

            var sortedDays = query.OrderByDescending(x => x.Activity_Date).Take(10);

            RecentActivty.Clear();

            foreach (var day in sortedDays)
            {
                decimal coh = 0;
                decimal pledge = 0;

                var dayGivings = givings.Where(x => x.Financial_Day_Id == day.Id);

                coh += dayGivings.Sum(x => x.Tithes_Amount + x.Offering_Amount);
                pledge += dayGivings.Sum(x => x.Solomon_Amount);

                foreach (var b in budgets.Where(x => x.Financial_Day_Id == day.Id))
                {
                    var sName = sourceLookup.ContainsKey(b.Source_Id) ? sourceLookup[b.Source_Id] : "";

                    if (sName == "pledge")
                        pledge += b.Amount;
                    else
                        coh += b.Amount;
                }

                foreach (var e in expenses.Where(x => x.Financial_Day_Id == day.Id))
                {
                    var sName = e.Source_Id.HasValue && sourceLookup.ContainsKey(e.Source_Id.Value)
    ? sourceLookup[e.Source_Id.Value]
    : "";

                    if (sName == "pledge")
                        pledge -= e.Amount;
                    else
                        coh -= e.Amount;
                }

                RecentActivty.Add(new FinancialDayViewModel
                {
                    Id = day.Id,
                    Date = day.Activity_Date,
                    Notes = day.Notes,
                    IsSunday = day.Is_Sunday,
                    CashOnHandNet = coh,
                    PledgeAmount = pledge
                });
            }

            //--------------------------------------
            // CHART DATA
            //--------------------------------------

            var labels = new List<string>();
            var incomeData = new List<decimal>();
            var expenseData = new List<decimal>();

            for (int i = 5; i >= 0; i--)
            {
                var d = DateTime.Now.AddMonths(-i);
                var start = new DateTime(d.Year, d.Month, 1);
                var end = start.AddMonths(1).AddDays(-1);

                labels.Add(d.ToString("MMM"));

                var monthDayIds = days
                    .Where(x => x.Activity_Date >= start && x.Activity_Date <= end)
                    .Select(x => x.Id)
                    .ToList();

                decimal inc = 0;
                decimal exp = 0;

                inc += budgets.Where(x => monthDayIds.Contains(x.Financial_Day_Id)).Sum(x => x.Amount);

                inc += givings
                    .Where(x => monthDayIds.Contains(x.Financial_Day_Id))
                    .Sum(x => x.Tithes_Amount + x.Offering_Amount + x.Solomon_Amount);

                exp += expenses.Where(x => monthDayIds.Contains(x.Financial_Day_Id)).Sum(x => x.Amount);

                incomeData.Add(inc);
                expenseData.Add(exp);
            }

            ChartLabels = labels.ToArray();
            ChartIncome = incomeData.ToArray();
            ChartExpense = expenseData.ToArray();
        }

        //--------------------------------------------------
        // POST HANDLERS
        //--------------------------------------------------

        public async Task<IActionResult> OnPostAddPlanAsync()
        {
            await _supabase.InitializeAsync(true);

            if (NewPlan.RecurrenceType == "FixedDay")
            {
                NewPlan.TargetDate = null;
                NewPlan.WeekNumber = null;
                NewPlan.DayOfWeekNumber = null;
            }
            else if (NewPlan.RecurrenceType == "NthWeekday")
            {
                NewPlan.TargetDate = null;
                NewPlan.DueDay = null;
            }
            else if (NewPlan.RecurrenceType == "OneTime")
            {
                NewPlan.DueDay = null;
                NewPlan.WeekNumber = null;
                NewPlan.DayOfWeekNumber = null;

                if (NewPlan.TargetDate.HasValue)
                    NewPlan.TargetDate = NewPlan.TargetDate.Value.Date.AddHours(12);
            }

            await _supabase.Client.From<PlannedExpense>().Insert(NewPlan);
            return RedirectToPage();
        }

        private DateTime GetNthWeekdayOfMonth(int year, int month, int weekNumber, DayOfWeek targetDay)
        {
            var firstDay = new DateTime(year, month, 1);

            int offset = ((int)targetDay - (int)firstDay.DayOfWeek + 7) % 7;
            var firstTargetDay = firstDay.AddDays(offset);

            var result = firstTargetDay.AddDays((weekNumber - 1) * 7);

            if (result.Month != month)
                throw new InvalidOperationException("Invalid week number for this month.");

            return result;
        }

        public async Task<IActionResult> OnPostUpdatePlanAsync()
        {
            await _supabase.InitializeAsync(true);

            if (EditPlan.RecurrenceType == "FixedDay")
            {
                EditPlan.TargetDate = null;
                EditPlan.WeekNumber = null;
                EditPlan.DayOfWeekNumber = null;
            }
            else if (EditPlan.RecurrenceType == "NthWeekday")
            {
                EditPlan.TargetDate = null;
                EditPlan.DueDay = null;
            }
            else if (EditPlan.RecurrenceType == "OneTime")
            {
                EditPlan.DueDay = null;
                EditPlan.WeekNumber = null;
                EditPlan.DayOfWeekNumber = null;

                if (EditPlan.TargetDate.HasValue)
                    EditPlan.TargetDate = EditPlan.TargetDate.Value.Date.AddHours(12);
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

        //--------------------------------------------------
        // VIEW MODELS
        //--------------------------------------------------

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