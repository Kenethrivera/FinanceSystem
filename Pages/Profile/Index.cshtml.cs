using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using FinanceSystem.Services;
using FinanceSystem.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication;

namespace FinanceSystem.Pages.Profile
{
    public class IndexModel : PageModel
    {
        private readonly SupabaseService _supabase;
        
        public IndexModel(SupabaseService supabase)
        {
            _supabase = supabase;
        }


        [BindProperty] 
        public string FullName { get; set; }
        [BindProperty] 
        public string Username { get; set; }

        [BindProperty]
        public string CurrentPassword { get; set; }

        [BindProperty]
        public string NewPassword { get; set; }

        [BindProperty]
        public string ConfirmPassword { get; set; }

        public string Role { get; set; }
        public string Message { get; set; }
        public bool IsSuccess { get; set; }

        public async Task OnGet()
        {
            await LoadUserData();
        }

        private async Task LoadUserData()
        {
            var currentUsername = User.Identity.Name;

            // Set Role Badge
            if (User.IsInRole("Admin")) Role = "Admin";
            else if (User.IsInRole("Auditor")) Role = "Auditor";
            else if (User.IsInRole("Pastor")) Role = "Senior Pastor";
            else if (User.IsInRole("Leader")) Role = "Leaders/Heads";
            else Role = "User";

            // Fetch real data from DB
            await _supabase.InitializeAsync(true);
            var users = await _supabase.Client.From<User>().Get();
            var currentUser = users.Models.FirstOrDefault(u => u.Username == currentUsername);

            if (currentUser != null)
            {
                Username = currentUser.Username;
                FullName = currentUser.Full_Name ?? currentUser.Username; // Fallback if empty
            }
        }

        public async Task<IActionResult> OnPostUpdateAccountAsync()
        {
            await _supabase.InitializeAsync(true);
            var oldUsername = User.Identity.Name;

            var users = await _supabase.Client.From<User>().Get();
            var user = users.Models.FirstOrDefault(u => u.Username == oldUsername);

            if (user != null)
            {
                user.Full_Name = FullName;

                // If they changed the username, we have a special case
                if (user.Username != Username)
                {
                    user.Username = Username;
                    await _supabase.Client.From<User>().Update(user);

                    // Force Logout because the "Cookie" still holds the old username
                    await HttpContext.SignOutAsync();
                    return RedirectToPage("/Login");
                }

                // Just updating name
                await _supabase.Client.From<User>().Update(user);
                IsSuccess = true;
                Message = "Profile updated successfully!";
            }

            // Reload data to refresh the page
            await LoadUserData();
            return Page();
        }

        public async Task<IActionResult> OnPostChangePasswordAsync()
        {
            await _supabase.InitializeAsync(true);
            var currentUsername = User.Identity.Name;

            if (NewPassword != ConfirmPassword)
            {
                IsSuccess = false;
                Message = "New passwords do not match.";
                await LoadUserData();
                return Page();
            }

            var users = await _supabase.Client.From<User>().Get();
            var user = users.Models.FirstOrDefault(u => u.Username == currentUsername);

            bool isCurrentCorrect = false;
            if (user != null)
            {
                if (user.Password.StartsWith("$2"))
                    try { isCurrentCorrect = BCrypt.Net.BCrypt.Verify(CurrentPassword, user.Password); } catch { }
                else
                    isCurrentCorrect = (user.Password == CurrentPassword);
            }
            if (!isCurrentCorrect)
            {
                IsSuccess = false;
                Message = "Incorrect current password.";
                await LoadUserData();
                return Page();
            }

            bool isSameAsOld = false;
            if (user.Password.StartsWith("$2"))
                isSameAsOld = BCrypt.Net.BCrypt.Verify(NewPassword, user.Password);
            else
                isSameAsOld = (NewPassword == user.Password);

            if (isSameAsOld)
            {
                IsSuccess = false;
                Message = "New password cannot be the same as the old password.";
                await LoadUserData();
                return Page();
            }

            user.Password = BCrypt.Net.BCrypt.HashPassword(NewPassword);
            await _supabase.Client.From<User>().Update(user);

            IsSuccess = true;
            Message = "Password updated securely! Next time, login with this new password.";
            await LoadUserData();
            return Page();
            
        }

    }
}
