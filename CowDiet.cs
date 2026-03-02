using System.Collections.Generic;

namespace CottonCowMod
{
    /// <summary>
    /// Defines which items are valid cow food for the CowTrough inventory.
    /// Finalized diet: all vegetables (minus mushrooms), grains, and apple.
    /// </summary>
    public static class CowDiet
    {
        /// <summary>
        /// Human-readable summary of what cows eat, shown when invalid items are placed.
        /// </summary>
        public const string DietDescription = "Must be a Vegetable, Grain or Apple";

        private static readonly HashSet<string> ValidCowFoods = new HashSet<string>
        {
            // Grains
            "Ingredient_Oats",
            "Ingredient_Barley",

            // Apple
            "Ingredient_Apple",

            // Vegetables (all game vegetables minus mushrooms)
            "Ingredient_Carrot",
            "Ingredient_Turnip",
            "Ingredient_Parsnip",
            "Ingredient_Potato_Brown",
            "Ingredient_Potato_Ruby",
            "Ingredient_Potato_White_Fluffy",
            "Ingredient_Cabbage",
            "Ingredient_Cabbage_Crests",
            "Ingredient_Cabbage_Sprouts",
            "Ingredient_Cauliflower",
            "Ingredient_Lettuce",
            "Ingredient_Pumpkin_Orange",
            "Ingredient_Pumpkin_Harvest_Crown",
            "Ingredient_Pumpkin_Squash",
            "Ingredient_Eggplant",
            "Ingredient_Tomato",
            "Ingredient_Box_Peppers",
            "Ingredient_Spinach",
            "Ingredient_Leek",
            "Ingredient_Peas",
            "Ingredient_Beans",
            "Ingredient_Baby_Marrow",
            "Ingredient_Radish",
            "Ingredient_Onion_Brown",
            "Ingredient_Onion_Shallot",
            "Ingredient_Fennel",
            "Ingredient_Cucumber",
            "Ingredient_Rhubarb",
            "Ingredient_Sperage",
            "Ingredient_Maize",
            "Ingredient_Hops",
        };

        public static bool IsValidCowFood(string itemTypeName)
        {
            return ValidCowFoods.Contains(itemTypeName);
        }
    }
}
