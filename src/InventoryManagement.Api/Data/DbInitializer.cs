using System;
using System.Linq;
using InventoryManagement.Shared;

namespace InventoryManagement.Api.Data
{
    public static class DbInitializer
    {
        public static void Initialize(InventoryDbContext context)
        {
            // Seed Categories
            if (!context.Categories.Any())
            {
                var categories = new[]
                {
                    new Category { Name = "Silk Sarees" },
                    new Category { Name = "Cotton Sarees" },
                    new Category { Name = "Readymades" },
                    new Category { Name = "Accessories" }
                };
                context.Categories.AddRange(categories);
            }

            // Seed Units
            if (!context.Units.Any())
            {
                var units = new[]
                {
                    new Unit { Code = "PCS", Name = "Pieces" },
                    new Unit { Code = "MTR", Name = "Meters" },
                    new Unit { Code = "KG", Name = "Kilograms" },
                    new Unit { Code = "BOX", Name = "Boxes" }
                };
                context.Units.AddRange(units);
            }

            context.SaveChanges();
        }
    }
}
