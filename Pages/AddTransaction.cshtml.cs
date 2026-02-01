//using Microsoft.AspNetCore.Mvc;
//using Microsoft.AspNetCore.Mvc.RazorPages;
//using FinanceSystem.Services;
//using Supabase;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Threading.Tasks;
//using FinanceSystem.Models;
//using System.Diagnostics;

//namespace FinanceSystem.Pages
//{
//    public class AddTransactionModel : PageModel
//    {
//        private readonly SupabaseService _supabaseService;

//        public AddTransactionModel(SupabaseService supabaseService)
//        {
//            _supabaseService = supabaseService;
//        }

//        public List<Category> Categories { get; set; } = new();
//        public List<Source> Sources { get; set; } = new();
//        public List<Sunday> Sundays { get; set; } = new();

//        [BindProperty]
//        public Transaction NewTransaction { get; set; } = new();

//        public async Task OnGetAsync()
//        {

//            if (_supabaseService.Client == null)
//            {
//                await _supabaseService.InitializeAsync();
//            }

//            Categories = (await _supabaseService.Client.From<Category>().Get()).Models;
//            Sources = (await _supabaseService.Client.From<Source>().Get()).Models;
//            Sundays = (await _supabaseService.Client.From<Sunday>().Get()).Models;

//            // Debug counts to ensure data is coming
//            Debug.WriteLine($"Categories count: {Categories.Count}");
//            Debug.WriteLine($"Sources count: {Sources.Count}");
//            Debug.WriteLine($"Sundays count: {Sundays.Count}");
//        }

//        public async Task<IActionResult> OnPostAsync()
//        {
//            if (!ModelState.IsValid)
//            {
//                await OnGetAsync();
//                return Page();
//            }

//            await _supabaseService.Client.From<Transaction>().Insert(NewTransaction);

//            return RedirectToPage("/Index");
//        }
//    }
//}
