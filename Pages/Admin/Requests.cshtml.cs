using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using FinanceSystem.Models;
using FinanceSystem.Services;

namespace FinanceSystem.Pages.Admin
{
    public class RequestsModel : PageModel
    {
        private readonly SupabaseService _supabase;
        public RequestsModel(SupabaseService supabase) { _supabase = supabase; }

        public List<FinancialRequest> PendingRequests { get; set; } = new();
        public decimal CurrentCashOnHand { get; set; } // For the "AI" Advice

        public async Task OnGetAsync()
        {
            await _supabase.InitializeAsync(true);

            // 1. Get Requests
            var response = await _supabase.Client.From<FinancialRequest>()
                .Where(r => r.Status == "Pending")
                .Get();
            PendingRequests = response.Models;

            // 2. Get Current Cash (For Advice Logic)
            // Reuse your Dashboard Calculation Logic here simply:
            var budgets = await _supabase.Client.From<Budget>().Get();
            var expenses = await _supabase.Client.From<Expenses>().Get();
            var sources = await _supabase.Client.From<Source>().Get();
            var memberGivings = await _supabase.Client.From<MemberGiving>().Get();

            decimal totalIncome = memberGivings.Models.Sum(g => g.Tithes_Amount + g.Offering_Amount);
            totalIncome += budgets.Models
                .Where(b => !(sources.Models.FirstOrDefault(s => s.Id == b.Source_Id)?.Name.Contains("Pledge") ?? false))
                .Sum(b => b.Amount);

            decimal totalExpenses = expenses.Models
                .Where(e => !(sources.Models.FirstOrDefault(s => s.Id == e.Source_Id)?.Name.Contains("Pledge") ?? false))
                .Sum(e => e.Amount);

            CurrentCashOnHand = totalIncome - totalExpenses;
        }

        public async Task<IActionResult> OnPostApproveAsync(long id, string title, decimal amount, DateTime dateNeeded, string adminComment)
        {
            await _supabase.InitializeAsync(true);

            // 1. Create Planned Expense
            // We force Noon (12:00:00) to prevent any UTC/Local shift from falling into the previous day
            var fixedDate = dateNeeded.Date.AddHours(12);

            var plan = new PlannedExpense
            {
                Title = title + " (Approved Request)",
                Amount = amount,
                IsMonthly = false,
                TargetDate = fixedDate
            };

            await _supabase.Client.From<PlannedExpense>().Insert(plan);

            // 2. Update Request Status
            var req = await _supabase.Client.From<FinancialRequest>().Where(x => x.Id == id).Single();
            if (req != null)
            {
                req.Status = "Approved";
                req.AdminComment = adminComment;
                // Also fix the request's own date just in case
                req.DateNeeded = fixedDate;
                await _supabase.Client.From<FinancialRequest>().Update(req);
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostRejectAsync(long id, string adminComment)
        {
            await _supabase.InitializeAsync(true);
            var req = await _supabase.Client.From<FinancialRequest>().Where(x => x.Id == id).Single();
            if (req != null)
            {
                req.Status = "Rejected";
                req.AdminComment = adminComment;
                await _supabase.Client.From<FinancialRequest>().Update(req);
            }
            return RedirectToPage();
        }
    }
}