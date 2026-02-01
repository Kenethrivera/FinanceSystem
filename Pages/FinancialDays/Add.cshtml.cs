using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using FinanceSystem.Models;
using FinanceSystem.ViewModels;
using FinanceSystem.Services;

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

            var categories = await _supabase.Client.From<Category>()
                .Where(c => c.Type == "Expenses")
                .Get();
            CategoryOptions = new SelectList(categories.Models, "Id", "Name");

            var sources = await _supabase.Client.From<Source>().Get();
            SourceOptions = new SelectList(sources.Models, "Id", "Name");

            var denominationsSetUp = new List<(int value, string Label)>
            { 
                (1000,  ""),
                (500, ""), 
                (200, ""), 
                (100, ""), 
                (50, ""), 
                (20, "Paper"), 
                (20, "Coins"), 
                (10, ""), 
                (5, ""), 
                (1, "") 
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

            for (int i = 0; i < 5; i++)
            {
                Input.ExpensesList.Add(new Expenses());
            }

            Input.Day.Activity_Date = DateTime.Today;
        }

        public async Task<IActionResult> OnPostAsync()
        {
            await _supabase.InitializeAsync(true);

            Input.Day.Activity_Date = Input.Day.Activity_Date.AddHours(12);

            var dayResponse = await _supabase.Client
                .From<Financial_Day>()
                .Insert(Input.Day);

            var newDay = dayResponse.Models.First();
            long newDayId = newDay.Id;

            bool hasBudget = false;
            if (Input.BudgetEntry.Amount > 0 && Input.BudgetEntry.Source_Id > 0)
            {
                Input.BudgetEntry.Financial_Day_Id = (int)newDayId;
                await _supabase.Client.From<Budget>().Insert(Input.BudgetEntry);
                hasBudget = true;
            }

            var validExpenses = Input.ExpensesList
                .Where(e => e.Amount > 0 && e.Category_Id > 0)
                .Select(e =>
                {
                    e.Financial_Day_Id = (int)newDayId;
                    e.Source_Id = e.Source_Id;
                    return e;
                }).ToList();

            if (validExpenses.Any())
            {
                await _supabase.Client.From<Expenses>().Insert(validExpenses);
            }

            var validOfferings = Input.OfferingList
                .Where(o => o.Quantity > 0)
                .Select(o =>
                {
                    o.Financial_Day_Id = (int)newDayId;
                    o.Total = o.Denomination * o.Quantity;
                    return o;
                }).ToList();

            if (validOfferings.Any())
            {
                await _supabase.Client.From<Offering>().Insert(validOfferings);
            }

            var detailsBuilder = new System.Text.StringBuilder();
            detailsBuilder.Append($"New Entry for {Input.Day.Activity_Date:MMM dd}: ");

            if (hasBudget)
            {
                detailsBuilder.Append($"[Income: ₱{Input.BudgetEntry.Amount:N0}] ");
            }
            if (validExpenses.Any())
            {
                detailsBuilder.Append("Expenses: ");
                decimal totalExp = validExpenses.Sum(e => e.Amount);
                detailsBuilder.Append($"[Total Exp: ₱{totalExp:N0} in {validExpenses.Count} items] ");
            }

            if (validOfferings.Any())
            {
                decimal totalOff = validOfferings.Sum(o => o.Total);
                detailsBuilder.Append($"[Offering: ₱{totalOff:N0}]");
            }
            await _supabase.LogActvity(User.Identity.Name, "CREATE", detailsBuilder.ToString());

            return RedirectToPage("/FinancialDays/Add");

        }


    }
}
