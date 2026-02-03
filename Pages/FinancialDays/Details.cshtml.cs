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

        // NEW: Added Envelopes List
        public List<MemberGiving> Envelopes { get; set; } = new();

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

            // 1. Get Day
            var response = await _supabase.Client
                .From<Financial_Day>()
                .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, id)
                .Single();

            if (response == null) return NotFound();
            Day = response;

            // 2. Get Related Data
            Budgets = (await _supabase.Client.From<Budget>().Filter("financial_day_id", Supabase.Postgrest.Constants.Operator.Equals, id).Get()).Models;
            Expenses = (await _supabase.Client.From<Expenses>().Filter("financial_day_id", Supabase.Postgrest.Constants.Operator.Equals, id).Get()).Models;
            Offerings = (await _supabase.Client.From<Offering>().Filter("financial_day_id", Supabase.Postgrest.Constants.Operator.Equals, id).Get()).Models;

            // NEW: Fetch Member Envelopes
            Envelopes = (await _supabase.Client.From<MemberGiving>().Filter("financial_day_id", Supabase.Postgrest.Constants.Operator.Equals, id).Get()).Models;

            // 3. Get Lookups
            Sources = (await _supabase.Client.From<Source>().Get()).Models;
            Categories = (await _supabase.Client.From<Category>().Get()).Models;

            // 4. Mother Church Logic
            var motherChurchWallet = Sources.FirstOrDefault(s => s.Name.Contains("Mother Church"));

            if (motherChurchWallet != null)
            {
                MotherChurchExpenseList = Expenses.Where(e => e.Source_Id == motherChurchWallet.Id).ToList();
                MotherChurchExpenses = MotherChurchExpenseList.Sum(e => e.Amount);

                MotherChurchIncome = Budgets
                    .Where(b => b.Source_Id == motherChurchWallet.Id)
                    .Sum(b => b.Amount);

                if (MotherChurchIncome > 0 || MotherChurchExpenses > 0)
                {
                    IsMotherChurchDay = true;
                    CashReturn = MotherChurchIncome - MotherChurchExpenses;
                }
            }

            // 5. Load Liquidation Image
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

            using var memoryStream = new MemoryStream();
            await uploadFile.CopyToAsync(memoryStream);
            var bytes = memoryStream.ToArray();
            var fileName = $"liquidation_{id}_{DateTime.Now.Ticks}.jpg";

            await _supabase.Client.Storage
                .From("liquidation-proofs")
                .Upload(bytes, fileName);

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

            // Delete all related records
            await _supabase.Client.From<Budget>().Filter("financial_day_id", Supabase.Postgrest.Constants.Operator.Equals, id).Delete();
            await _supabase.Client.From<Expenses>().Filter("financial_day_id", Supabase.Postgrest.Constants.Operator.Equals, id).Delete();
            await _supabase.Client.From<Offering>().Filter("financial_day_id", Supabase.Postgrest.Constants.Operator.Equals, id).Delete();
            await _supabase.Client.From<MemberGiving>().Filter("financial_day_id", Supabase.Postgrest.Constants.Operator.Equals, id).Delete(); // NEW
            await _supabase.Client.From<LiquidationReport>().Filter("financial_day_id", Supabase.Postgrest.Constants.Operator.Equals, id).Delete();

            await _supabase.Client.From<Financial_Day>().Filter("id", Supabase.Postgrest.Constants.Operator.Equals, id).Delete();
            await _supabase.LogActvity(User.Identity.Name, "DELETE", logDetails);

            return RedirectToPage("/Index");
        }

        public async Task<IActionResult> OnPostUpdateNotesAsync(int id, string newNotes)
        {
            await _supabase.InitializeAsync(true);

            var day = await _supabase.Client
                .From<Financial_Day>()
                .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, id)
                .Single();

            if (day != null)
            {
                day.Notes = newNotes;
                day.Activity_Date = day.Activity_Date.Date.AddHours(12);
                await _supabase.Client.From<Financial_Day>().Update(day);

                string logDetail = $"Updated Notes for {day.Activity_Date:MMM dd}. Notes: '{newNotes}'";
                await _supabase.LogActvity(User.Identity.Name, "UPDATE", logDetail);
            }

            return RedirectToPage("/Index"); // Or back to Details
        }

        public async Task<IActionResult> OnPostFlagRecordAsync(int id, string comment)
        {
            await _supabase.InitializeAsync(true);
            var day = await _supabase.Client.From<Financial_Day>().Filter("id", Supabase.Postgrest.Constants.Operator.Equals, id).Single();

            if (day != null)
            {
                day.FlagComment = comment;
                day.FlagResponse = null;
                day.Activity_Date = day.Activity_Date.Date.AddHours(12);
                await _supabase.Client.From<Financial_Day>().Update(day);

                await _supabase.LogActvity(User.Identity.Name, "UPDATE", $"Flagged Entry {day.Activity_Date:MMM dd}. Issue: '{comment}'");
            }
            return RedirectToPage(new { id = id });
        }

        public async Task<IActionResult> OnPostResolveFlagAsync(int id, string adminResponse)
        {
            await _supabase.InitializeAsync(true);
            var day = await _supabase.Client.From<Financial_Day>().Filter("id", Supabase.Postgrest.Constants.Operator.Equals, id).Single();

            if (day != null)
            {
                day.FlagResponse = adminResponse;
                day.Activity_Date = day.Activity_Date.Date.AddHours(12);
                await _supabase.Client.From<Financial_Day>().Update(day);

                await _supabase.LogActvity(User.Identity.Name, "UPDATE", $"Resolved Flag for {day.Activity_Date:MMM dd}. Fix: '{adminResponse}'");
            }
            return RedirectToPage(new { id = id });
        }
    }
}