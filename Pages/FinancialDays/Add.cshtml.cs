using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using FinanceSystem.Models;
using FinanceSystem.ViewModels;
using FinanceSystem.Services;

namespace FinanceSystem.Pages.FinancialDays
{
    public class AddModel : PageModel
    {
        private readonly SupabaseService _supabase;

        public AddModel(SupabaseService supabase)
        {
            _supabase = supabase;
        }

        [BindProperty]
        public FinancialEntryViewModel Input { get; set; } = new();

        public SelectList CategoryOptions { get; set; }
        public SelectList SourceOptions { get; set; }

        public async Task OnGetAsync()
        {
            await _supabase.InitializeAsync(true);

            // 1. Setup Dropdowns
            var categories = await _supabase.Client.From<Category>().Where(c => c.Type == "Expenses").Get();
            CategoryOptions = new SelectList(categories.Models, "Id", "Name");

            var sources = await _supabase.Client.From<Source>().Get();
            SourceOptions = new SelectList(sources.Models, "Id", "Name");

            // 2. Setup Rows
            for (int i = 0; i < 20; i++) Input.EnvelopeList.Add(new MemberGiving());

            var denominationsSetUp = new List<(int value, string Label)>
            {
                (1000,  ""), (500, ""), (200, ""), (100, ""), (50, ""),
                (20, "Paper"), (20, "Coins"), (10, ""), (5, ""), (1, "")
            };

            foreach (var item in denominationsSetUp)
            {
                Input.OfferingList.Add(new Offering
                {
                    Denomination = item.value,
                    Notes = item.Label,
                    Quantity = 0
                });
            }

            for (int i = 0; i < 5; i++) Input.ExpensesList.Add(new Expenses());

            Input.Day.Activity_Date = DateTime.Today;
        }

        public async Task<IActionResult> OnPostAsync()
        {
            await _supabase.InitializeAsync(true);

            // --- 1. CLEANUP & PREPARATION ---

            // Filter Envelopes (User must have typed a name OR an amount)
            var validEnvelopes = Input.EnvelopeList
                .Where(e => !string.IsNullOrWhiteSpace(e.Member_Name) || e.Total_Amount > 0)
                .ToList();

            // Filter Expenses (Must have an amount)
            var validExpenses = Input.ExpensesList
                .Where(e => e.Amount > 0)
                .ToList();

            // Filter Cash/Offerings (Must have quantity) & Calculate Totals
            var validOfferings = Input.OfferingList
                .Where(o => o.Quantity > 0)
                .Select(o => { o.Total = o.Denomination * o.Quantity; return o; })
                .ToList();

            // Check Budget
            bool hasBudget = Input.BudgetEntry.Amount > 0;

            // --- 2. EMPTY FORM CHECK ---
            // If the user didn't fill in ANYTHING, stop them.
            if (!hasBudget && !validExpenses.Any() && !validEnvelopes.Any())
            {
                ModelState.AddModelError("", "You cannot save an empty entry. Please fill in at least one section (Budget, Expense, or Offering).");
                return await ReloadPage();
            }

            // --- 3. VALIDATION: CASH BALANCING ---
            // Rule: IF you entered envelopes, the Total Envelopes MUST match the Total Physical Cash.
            // If you only entered Expenses (and validEnvelopes is 0), we SKIP this check.
            if (validEnvelopes.Any())
            {
                decimal totalEnvelopes = validEnvelopes.Sum(e => e.Total_Amount);
                decimal totalPhysicalCash = validOfferings.Sum(o => o.Total);

                if (totalEnvelopes != totalPhysicalCash)
                {
                    ModelState.AddModelError("", $"Cash Mismatch! Envelopes Total (₱{totalEnvelopes:N0}) does not match Physical Cash Count (₱{totalPhysicalCash:N0}). Please recount.");
                    return await ReloadPage();
                }
            }

            // --- 4. SAVE TO DATABASE ---

            // A. Save Day
            // If Sunday, mark it
            if (Input.Day.Activity_Date.DayOfWeek == DayOfWeek.Sunday) Input.Day.Is_Sunday = true;

            // Fix Timezone/Date issue
            Input.Day.Activity_Date = Input.Day.Activity_Date.Date.AddHours(12);

            var dayResponse = await _supabase.Client.From<Financial_Day>().Insert(Input.Day);
            var newDay = dayResponse.Models.FirstOrDefault();

            if (newDay == null)
            {
                ModelState.AddModelError("", "Database Error: Could not create Financial Day.");
                return await ReloadPage();
            }

            int dayId = newDay.Id;

            // B. Save Budget (If exists)
            if (hasBudget)
            {
                Input.BudgetEntry.Financial_Day_Id = dayId;
                await _supabase.Client.From<Budget>().Insert(Input.BudgetEntry);
            }

            // C. Save Envelopes (If any)
            if (validEnvelopes.Any())
            {
                validEnvelopes.ForEach(e => e.Financial_Day_Id = dayId);
                await _supabase.Client.From<MemberGiving>().Insert(validEnvelopes);
            }

            // D. Save Expenses (If any)
            if (validExpenses.Any())
            {
                validExpenses.ForEach(e => e.Financial_Day_Id = dayId);
                await _supabase.Client.From<Expenses>().Insert(validExpenses);
            }

            // E. Save Cash Count (If any)
            if (validOfferings.Any())
            {
                validOfferings.ForEach(o => o.Financial_Day_Id = dayId);
                await _supabase.Client.From<Offering>().Insert(validOfferings);
            }

            // --- 5. LOGGING & REDIRECT ---
            string log = $"Added Entry {newDay.Activity_Date:MMM dd}. ";
            if (hasBudget) log += "Has Budget. ";
            if (validExpenses.Any()) log += $"Exp: {validExpenses.Count}. ";
            if (validEnvelopes.Any()) log += $"Env: {validEnvelopes.Count}.";

            await _supabase.LogActvity(User.Identity.Name, "CREATE", log);

            return RedirectToPage("/FinancialDays/Add");
        }

        // Helper to reload dropdowns if validation fails
        private async Task<IActionResult> ReloadPage()
        {
            await _supabase.InitializeAsync(true);
            var categories = await _supabase.Client.From<Category>().Where(c => c.Type == "Expenses").Get();
            CategoryOptions = new SelectList(categories.Models, "Id", "Name");

            var sources = await _supabase.Client.From<Source>().Get();
            SourceOptions = new SelectList(sources.Models, "Id", "Name");
            return Page();
        }
    }
}