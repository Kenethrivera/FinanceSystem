using FinanceSystem.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;
using FinanceSystem.Models;

namespace FinanceSystem.Pages.Settings
{
    public class IndexModel : PageModel
    {
        private readonly SupabaseService _supabase;

        public IndexModel(SupabaseService supabase)
        {
            _supabase = supabase;
        }

        public List<Category> IncomeCategories { get; set; } = new();
        public List<Category> ExpenseCategories { get; set; } = new();
        public List<Source> Sources { get; set; } = new();
            
        public async Task<IActionResult> OnGetAsync()
        {
            if (!User.IsInRole("Admin")) return Forbid();
            await _supabase.InitializeAsync(true);
            await LoadData();
            return Page();
        }

        private async Task LoadData()
        {
            var allCats = (await _supabase.Client.From<Category>().Get()).Models;

            // 1. Separate Income Types (e.g. Offering)
            IncomeCategories = allCats
                .Where(c => c.Type == "Income")
                .OrderBy(c => c.Name)
                .ToList();

            // 2. Separate Expense Categories (e.g. Food)
            ExpenseCategories = allCats
                .Where(c => c.Type == "Expenses")
                .OrderBy(c => c.Name)
                .ToList();

            Sources = (await _supabase.Client.From<Source>().Get()).Models.OrderBy(s => s.Name).ToList();
        }

        // --- HANDLER: ADD INCOME CATEGORY (NEW) ---
        public async Task<IActionResult> OnPostAddIncomeCategoryAsync(string name)
        {
            await _supabase.InitializeAsync(true);
            var newCat = new Category { Name = name, Type = "Income" }; // Set Type to Income
            await _supabase.Client.From<Category>().Insert(newCat);
            return RedirectToPage();
        }

        // --- HANDLER: ADD EXPENSE CATEGORY ---
        public async Task<IActionResult> OnPostAddCategoryAsync(string name)
        {
            await _supabase.InitializeAsync(true);
            var newCat = new Category { Name = name, Type = "Expenses" }; // Set Type to Expenses
            await _supabase.Client.From<Category>().Insert(newCat);
            return RedirectToPage();
        }

        // --- HANDLER: ADD WALLET ---
        public async Task<IActionResult> OnPostAddSourceAsync(string name, string description)
        {
            await _supabase.InitializeAsync(true);
            var newSource = new Source { Name = name, Description = !string.IsNullOrEmpty(description) ? description : "User Added Wallet" };
            await _supabase.Client.From<Source>().Insert(newSource);
            return RedirectToPage();
        }

        // --- DELETE HANDLERS
        public async Task<IActionResult> OnPostDeleteCategoryAsync(int id)
        {
            await _supabase.InitializeAsync(true);
            await _supabase.Client.From<Category>().Filter("id", Supabase.Postgrest.Constants.Operator.Equals, id).Delete();
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteSourceAsync(int id)
        {
            await _supabase.InitializeAsync(true);
            await _supabase.Client.From<Source>().Filter("id", Supabase.Postgrest.Constants.Operator.Equals, id).Delete();
            return RedirectToPage();
        }

    }
}
