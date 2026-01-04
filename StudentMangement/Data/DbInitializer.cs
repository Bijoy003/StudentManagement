using Microsoft.AspNetCore.Identity;

namespace StudentMangement.Data
{
    public static class DbInitializer
    {
        public static async Task SeedAdmin(IServiceProvider serviceProvider)
        {
            var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();

            // Create roles if they dont exist
            string[] roleNames = { "Admin", "Teacher", "Student", "User" };

            foreach (var roleName in roleNames)
            {
                // Check if the role exists
                if (!await roleManager.RoleExistsAsync(roleName))
                {
                    await roleManager.CreateAsync(new IdentityRole(roleName));
                }
            }

            // Create Admin user if it doesn't exist
            var admin = await userManager.FindByEmailAsync("admin@admin.com");
            if (admin == null)
            {
                admin = new ApplicationUser { UserName = "admin@admin.com", Email = "admin@admin.com", FullName = "Admin User" };
                var result = await userManager.CreateAsync(admin, "Admin123"); // Password is hashed automatically
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(admin, "Admin");
                }
            }

            // Create Admin user if it doesn't exist
            var su = await userManager.FindByEmailAsync("bhugolbijoy003@gmail.com");
            if (su == null)
            {
                su = new ApplicationUser { UserName = "bhugolbijoy003@gmail.com", Email = "bhugolbijoy003@gmail.com", FullName = "Super Admin User", EmailConfirmed = true };
                var result = await userManager.CreateAsync(su, "Admin123"); // Password is hashed automatically
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(su, "Admin");
                }
            }
        }
    }
}
