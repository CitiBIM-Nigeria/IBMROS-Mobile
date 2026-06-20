using System.Collections.Generic;

public class SubcategoryModel
{
    public string SubcategoryId   { get; set; }
    public string SubcategoryName { get; set; }
    public string SubcategoryUrl  { get; set; }
    public string Icon            { get; set; }   // emoji from ros-categories (may be empty)
}

public class CategoryModel
{
    public string MerchantId   { get; set; }
    public string CategoryId   { get; set; }
    public string CategoryName { get; set; }
    public string CategoryUrl  { get; set; }
    public string Icon         { get; set; }   // emoji from ros-categories
    public string ParentId     { get; set; }   // department id (parent_category_id)
    public List<SubcategoryModel> Subcategories { get; set; } = new();
}