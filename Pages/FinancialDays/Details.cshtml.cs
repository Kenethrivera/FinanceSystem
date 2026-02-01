using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using FinanceSystem.Services;
using FinanceSystem.Models;

namespace FinanceSystem.Pages.FinancialDays
{
    public class DetailsModel : PageModel
    {
        private readonly SupabaseService _supabase;

        public DetailsModel(SupabaseService supabase)
        {
            _supabase = supabase;
        }

        public Financial_Day Day { get; set; }
        public List<Budget> Budgets { get; set; } = new();
        public List<Expenses> Expenses { get; set; } = new();
        public List<Offering> Offerings { get; set; } = new();
        public List<Source> Sources { get; set; } = new();
        public List<Category> Categories { get; set; } = new();

        public LiquidationReport Liquidation { get; set; }
        public bool IsMotherChurchDay { get; set; }
        public decimal MotherChurchIncome { get; set; }
        public decimal MotherChurchExpenses { get; set; }
        public decimal CashReturn { get; set; }
        public List<Expenses> MotherChurchExpenseList { get; set; } = new();

        public async Task<IActionResult> OnGetAsync(int id)
        {
            await _supabase.InitializeAsync(true);

            var response = await _supabase.Client
                .From<Financial_Day>()
                .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, id)
                .Single();

            if (response == null) return NotFound();
            Day = response;

            Budgets = (await _supabase.Client.From<Budget>().Filter("financial_day_id", Supabase.Postgrest.Constants.Operator.Equals, id).Get()).Models;
            Expenses = (await _supabase.Client.From<Expenses>().Filter("financial_day_id", Supabase.Postgrest.Constants.Operator.Equals, id).Get()).Models;
            Offerings = (await _supabase.Client.From<Offering>().Filter("financial_day_id", Supabase.Postgrest.Constants.Operator.Equals, id).Get()).Models;

            Sources = (await _supabase.Client.From<Source>().Get()).Models;
            Categories = (await _supabase.Client.From<Category>().Get()).Models;

            var motherChurchWallet = Sources.FirstOrDefault(s => s.Name.Contains("Mother Church"));

            if (motherChurchWallet != null)
            {
                // Did we receive money FROM Mother Church?
                var incomeFromMC = Budgets.Where(b => b.Source_Id == motherChurchWallet.Id).ToList(); // Note: Budget Source_Id is Destination, verify logic based on your usage
                                                                                                      // Actually, usually Budgets are "Category" -> "Wallet".
                                                                                                      // If "Mother Church" is a Wallet, we check if money went INTO it, or if you have a "Mother Church Support" CATEGORY.
                                                                                                      // Let's assume you track it by WALLET used for Expenses.

                // Let's look at EXPENSES paid BY Mother Church Wallet
                MotherChurchExpenseList = Expenses.Where(e => e.Source_Id == motherChurchWallet.Id).ToList();
                MotherChurchExpenses = MotherChurchExpenseList.Sum(e => e.Amount);

                // Calculate "Budget" given (Income)
                // This depends on how you record it. If you put "Mother Church Support" into "Mother Church Wallet":
                MotherChurchIncome = Budgets
                    .Where(b => b.Source_Id == motherChurchWallet.Id)
                    .Sum(b => b.Amount);

                if (MotherChurchIncome > 0 || MotherChurchExpenses > 0)
                {
                    IsMotherChurchDay = true;
                    CashReturn = MotherChurchIncome - MotherChurchExpenses;
                }
            }

            // 2. Load Existing Liquidation Report (Image)
            var reports = await _supabase.Client.From<LiquidationReport>()
                .Filter("financial_day_id", Supabase.Postgrest.Constants.Operator.Equals, id)
                .Get();
            Liquidation = reports.Models.FirstOrDefault();

            return Page();
        }

        public async Task<IActionResult> OnPostUploadLiquidationAsync(int id, IFormFile uploadFile)
        {
            if (uploadFile == null) return RedirectToPage(new { id = id });

            await _supabase.InitializeAsync(true);

            // 1. Upload to Supabase Storage
            using var memoryStream = new MemoryStream();
            await uploadFile.CopyToAsync(memoryStream);
            var bytes = memoryStream.ToArray();
            var fileName = $"liquidation_{id}_{DateTime.Now.Ticks}.jpg";

            await _supabase.Client.Storage
                .From("liquidation-proofs")
                .Upload(bytes, fileName);

            // 2. Save Link to Database
            var report = new LiquidationReport
            {
                Financial_Day_Id = id,
                ImagePath = fileName
            };
            await _supabase.Client.From<LiquidationReport>().Insert(report);

            return RedirectToPage(new { id = id });
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            await _supabase.InitializeAsync(true);

            var dayToDelete = (await _supabase.Client
                .From<Financial_Day>()
                .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, id)
                .Get()).Models.FirstOrDefault();

            string logDetails = "Deleted Financial Day ID: " + id;

            if (dayToDelete != null)
            {
                logDetails = $"Deleted Entry for {dayToDelete.Activity_Date:MMM dd, yyyy}";
            }
            await _supabase.Client.From<Budget>().Filter("financial_day_id", Supabase.Postgrest.Constants.Operator.Equals, id).Delete();
            await _supabase.Client.From<Expenses>().Filter("financial_day_id", Supabase.Postgrest.Constants.Operator.Equals, id).Delete();
            await _supabase.Client.From<Offering>().Filter("financial_day_id", Supabase.Postgrest.Constants.Operator.Equals, id).Delete();

            await _supabase.Client.From<Financial_Day>().Filter("id", Supabase.Postgrest.Constants.Operator.Equals, id).Delete();
            await _supabase.LogActvity(User.Identity.Name, "DELETE", logDetails);

            return RedirectToPage("/Index");
        }

        public async Task<IActionResult> OnPostUpdateNotesAsync(int id, string newNotes)
        {
            await _supabase.InitializeAsync(true);

            var oldData = (await _supabase.Client.From<Financial_Day>()
                .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, id)
                .Get()).Models.FirstOrDefault();



            var day = await _supabase.Client
                .From<Financial_Day>()
                .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, id)
                .Single();

            if (day != null)
            {
                string oldNotes = day.Notes;
                day.Notes = newNotes;

                day.Activity_Date = day.Activity_Date.Date.AddHours(12);

                await _supabase.Client
                    .From<Financial_Day>()
                    .Update(day);

                string logDetail = $"Updated Notes for {day.Activity_Date:MMM dd}. Notes: '{newNotes}'";
                await _supabase.LogActvity(User.Identity.Name, "UPDATE", logDetail);
            }

            return RedirectToPage("/Index");
        }

        // 1. AUDITOR: Sets the flag with a comment
        public async Task<IActionResult> OnPostFlagRecordAsync(int id, string comment)
        {
            await _supabase.InitializeAsync(true);

            var day = await _supabase.Client
                .From<Financial_Day>()
                .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, id)
                .Single();

            if (day != null)
            {
                day.FlagComment = comment;
                day.FlagResponse = null;

                day.Activity_Date = day.Activity_Date.Date.AddHours(12);

                await _supabase.Client.From<Financial_Day>().Update(day);
                string logDetail = $"Flagged Entry {day.Activity_Date:MMM dd}. Issue: '{comment}'";
                await _supabase.LogActvity(User.Identity.Name, "UPDATE", logDetail);
            }

            return RedirectToPage(new { id = id });
        }

        // 2. ADMIN: Clears the flag (Marks as Resolved)
        public async Task<IActionResult> OnPostResolveFlagAsync(int id, string adminResponse)
        {
            await _supabase.InitializeAsync(true);

            var day = await _supabase.Client
                .From<Financial_Day>()
                .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, id)
                .Single();

            if (day != null)
            {
                day.FlagResponse = adminResponse;
                day.Activity_Date = day.Activity_Date.Date.AddHours(12);
                await _supabase.Client.From<Financial_Day>().Update(day);
                string logDetail = $"Resolved Flag for {day.Activity_Date:MMM dd}. Fix: '{adminResponse}'";
                await _supabase.LogActvity(User.Identity.Name, "UPDATE", logDetail);
            }

            return RedirectToPage(new { id = id });
        }


    }
}