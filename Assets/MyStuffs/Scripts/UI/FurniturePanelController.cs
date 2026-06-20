using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using System;
using System.Threading.Tasks;
using UnityEngine.Networking;

public class FurniturePanelController : MonoBehaviour
{
    public event System.Action OnPanelClosed;
    public event System.Action<string> OnItemSelected;

    // ---------------------------------------------------------------
    // UI REFERENCES
    // ---------------------------------------------------------------

    private VisualElement _panel;
    private VisualElement _categoryGrid;
    private VisualElement _body;
    private VisualElement _tabs;
    private Button        _tabRoom;
    private Button        _tabOtherRooms;
    private Label         _titleLabel;
    private Button        _backButton;
    private Button        _searchButton;

    // ---------------------------------------------------------------
    // STATE
    // ---------------------------------------------------------------

    private bool      _isOpen         = false;
    private bool      _hasStoredState = false;
    private bool      _isRoomTab      = true;
    private Coroutine _animationCoroutine;
    private const float SLIDE_DURATION = 0.3f;

    // Navigation stack — each entry remembers what level we are on
    private class NavEntry
    {
        public string Title;
        public string CategoryId;
        public string SubcategoryId; // null at category level
        public bool   IsItemList;
    }
    private readonly Stack<NavEntry> _navStack = new();

    // Cached data so back navigation does not re-fetch
    private List<CategoryModel> _cachedCategories;
    private List<ProductModel>  _cachedProducts;
    private string              _activeCategoryId;
    private string              _activeSubcategoryId;
    
    private Dictionary<VisualElement, bool> _shimmeringElements = new();

    // ---------------------------------------------------------------
    // INITIALIZE
    // ---------------------------------------------------------------

    public void Initialize(VisualElement root)
    {
        _panel         = root.Q<VisualElement>("FurniturePanel");
        _body          = root.Q<VisualElement>("FurniturePanelBody");
        _categoryGrid  = root.Q<VisualElement>("FurnitureCategoryGrid");
        _tabs          = root.Q<VisualElement>("FurniturePanelTabs");
        _tabRoom       = root.Q<Button>("TabRoom");
        _tabOtherRooms = root.Q<Button>("TabOtherRooms");
        _titleLabel    = root.Q<Label>("FurniturePanelTitle");
        _backButton    = root.Q<Button>("FurniturePanelBackButton");
        _searchButton  = root.Q<Button>("FurniturePanelSearchButton");

        if (_panel == null)
            Debug.LogError("[FurniturePanelController] FurniturePanel not found.");

        _panel?.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());

        var categoryScroll = _panel?.Q<ScrollView>("FurniturePanelScroll");
        if (categoryScroll != null)
        {
            categoryScroll.mouseWheelScrollSize         = 100f;
            categoryScroll.verticalScrollerVisibility   = ScrollerVisibility.Hidden;
            categoryScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        }

        _tabRoom?.RegisterCallback<ClickEvent>(evt => SwitchTab(true));
        _tabOtherRooms?.RegisterCallback<ClickEvent>(evt => SwitchTab(false));
        _backButton?.RegisterCallback<ClickEvent>(evt => NavigateBack());

        // Subscribe to data service events
        FurnitureDataService.OnCategoriesLoaded   += HandleCategoriesLoaded;
        FurnitureDataService.OnCategoriesFailed   += HandleDataFailed;
        FurnitureDataService.OnProductsLoaded     += HandleProductsLoaded;
        FurnitureDataService.OnProductsFailed     += HandleDataFailed;
        FurnitureDataService.OnLoadingChanged     += HandleLoadingChanged;
    }

    void OnDestroy()
    {
        FurnitureDataService.OnCategoriesLoaded   -= HandleCategoriesLoaded;
        FurnitureDataService.OnCategoriesFailed   -= HandleDataFailed;
        FurnitureDataService.OnProductsLoaded     -= HandleProductsLoaded;
        FurnitureDataService.OnProductsFailed     -= HandleDataFailed;
        FurnitureDataService.OnLoadingChanged     -= HandleLoadingChanged;
    }

    // ---------------------------------------------------------------
    // TAB SWITCHING
    // ---------------------------------------------------------------

    private void SwitchTab(bool isRoomTab)
    {
        _isRoomTab = isRoomTab;

        if (isRoomTab)
        {
            _tabRoom?.AddToClassList("furniture-tab--active");
            _tabOtherRooms?.RemoveFromClassList("furniture-tab--active");
        }
        else
        {
            _tabOtherRooms?.AddToClassList("furniture-tab--active");
            _tabRoom?.RemoveFromClassList("furniture-tab--active");
        }

        if (_cachedCategories != null)
            BuildCategoryGrid(FilterCategoriesForTab(_cachedCategories));
        else
            _ = FurnitureDataService.Instance.LoadCategories();
    }

    // Departments shown under the "Other Rooms" tab; everything else (seating,
    // tables, lighting, beds, …) shows under the main "Room" tab. Split is by the
    // category's parent department id from ros-categories — adjust to taste.
    private static readonly HashSet<string> OtherRoomDepartments = new()
    {
        "storage", "bathroom", "outdoor",
    };

    private List<CategoryModel> FilterCategoriesForTab(List<CategoryModel> all)
    {
        return _isRoomTab
            ? all.FindAll(c => !OtherRoomDepartments.Contains(c.ParentId))
            : all.FindAll(c =>  OtherRoomDepartments.Contains(c.ParentId));
    }

    // ---------------------------------------------------------------
    // NAVIGATION
    // ---------------------------------------------------------------

    private void NavigateTo(CategoryModel category)
    {
        _activeCategoryId    = category.CategoryId;
        _activeSubcategoryId = null;

        string title = CategoryMapper.GetAppCategoryName(category.CategoryId)
                       ?? category.CategoryName;

        _navStack.Push(new NavEntry
        {
            Title      = title,
            CategoryId = category.CategoryId,
            IsItemList = false,
        });

        UpdateHeader(title);

        if (category.Subcategories != null && category.Subcategories.Count > 0)
        {
            BuildSubcategoryGrid(category.Subcategories, category.CategoryId);

            // Prefetch ALL subcategory products in parallel
            _ = PrefetchSubcategoryProducts(category);
        }
        else
            _ = FurnitureDataService.Instance
                .LoadProductsByCategory(category.CategoryId);
    }

    private void NavigateToSubcategory(SubcategoryModel sub, string categoryId)
    {
        _activeSubcategoryId = sub.SubcategoryId;

        _navStack.Push(new NavEntry
        {
            Title         = sub.SubcategoryName,
            CategoryId    = categoryId,
            SubcategoryId = sub.SubcategoryId,
            IsItemList    = true,
        });

        UpdateHeader(sub.SubcategoryName);

        _ = FurnitureDataService.Instance
            .LoadProductsBySubcategory(categoryId, sub.SubcategoryId);
    }

    private void NavigateBack()
    {
        if (_navStack.Count == 0) return;

        _navStack.Pop();

        if (_navStack.Count == 0)
        {
            UpdateHeader(null);
            CleanItemListViews();
            ShowCategoryScroll();

            if (_cachedCategories != null)
                BuildCategoryGrid(FilterCategoriesForTab(_cachedCategories));
            else
                _ = FurnitureDataService.Instance.LoadCategories();
            return;
        }

        var entry = _navStack.Peek();
        UpdateHeader(entry.Title);
        _activeCategoryId    = entry.CategoryId;
        _activeSubcategoryId = entry.SubcategoryId;

        if (entry.IsItemList && _cachedProducts != null)
        {
            CleanItemListViews();
            HideCategoryScroll();
            BuildItemList(_cachedProducts);
        }
        else
        {
            CleanItemListViews();
            ShowCategoryScroll();

            var category = _cachedCategories?.Find(
                c => c.CategoryId == entry.CategoryId);
            if (category?.Subcategories != null)
                BuildSubcategoryGrid(category.Subcategories, entry.CategoryId);
        }
    }

    private void UpdateHeader(string title)
    {
        bool atRoot = (title == null);

        if (_titleLabel != null)
            _titleLabel.text = atRoot ? "Room" : title;

        if (_backButton != null)
        {
            if (atRoot)
                _backButton.RemoveFromClassList("furniture-panel__back-btn--visible");
            else
                _backButton.AddToClassList("furniture-panel__back-btn--visible");
        }

        if (_tabs != null)
            _tabs.style.display = atRoot ? DisplayStyle.Flex : DisplayStyle.None;
    }

    // ---------------------------------------------------------------
    // DATA SERVICE HANDLERS
    // ---------------------------------------------------------------

    private void HandleCategoriesLoaded(List<CategoryModel> categories)
    {
        _cachedCategories = categories;
        BuildCategoryGrid(FilterCategoriesForTab(categories));
    }

    private void HandleProductsLoaded(List<ProductModel> products, string context)
    {
        _cachedProducts = products;
        CleanItemListViews();
        HideCategoryScroll();
        BuildItemList(products);
    }

    private void HandleDataFailed(string message)
    {
        ShowErrorState(message);
    }

    private void HandleLoadingChanged(bool isLoading, string message)
    {
        if (isLoading)
            ShowLoadingState(message);
    }

    // ---------------------------------------------------------------
    // CATEGORY GRID
    // ---------------------------------------------------------------

    private void BuildCategoryGrid(List<CategoryModel> categories)
    {
        if (_categoryGrid == null) return;

        _categoryGrid.Clear();
        CleanItemListViews();
        ShowCategoryScroll();

        if (categories == null || categories.Count == 0)
        {
            ShowEmptyState("No categories available.");
            return;
        }

        for (int i = 0; i < categories.Count; i += 2)
        {
            var row = new VisualElement();
            row.AddToClassList("category-grid-row");

            row.Add(CreateCategoryCard(categories[i]));

            if (i + 1 < categories.Count)
                row.Add(CreateCategoryCard(categories[i + 1]));
            else
                row.Add(MakeSpacer());

            _categoryGrid.Add(row);
        }
    }

    private Button CreateCategoryCard(CategoryModel category)
    {
        string appName = category.CategoryName;
        string emoji   = !string.IsNullOrEmpty(category.Icon)
                         ? category.Icon
                         : CategoryMapper.GetCategoryEmoji(appName);

        var card = new Button();
        card.AddToClassList("category-card");

        var iconArea = new VisualElement();
        iconArea.AddToClassList("category-card__icon-area");

        var emojiLabel = new Label(emoji);
        emojiLabel.AddToClassList("category-card__emoji");

        var nameLabel = new Label(appName);
        nameLabel.AddToClassList("category-card__name");

        iconArea.Add(emojiLabel);
        card.Add(iconArea);
        card.Add(nameLabel);

        card.RegisterCallback<ClickEvent>(evt =>
        {
            evt.StopPropagation();
            NavigateTo(category);
        });

        return card;
    }

    // ---------------------------------------------------------------
    // SUBCATEGORY GRID
    // ---------------------------------------------------------------

    private void BuildSubcategoryGrid(
        List<SubcategoryModel> subcategories, string categoryId)
    {
        if (_categoryGrid == null) return;

        _categoryGrid.Clear();
        ShowCategoryScroll();

        string emoji = CategoryMapper.GetCategoryEmoji(categoryId);

        for (int i = 0; i < subcategories.Count; i += 2)
        {
            var row = new VisualElement();
            row.AddToClassList("category-grid-row");

            row.Add(CreateSubcategoryCard(subcategories[i], categoryId, emoji));

            if (i + 1 < subcategories.Count)
                row.Add(CreateSubcategoryCard(
                    subcategories[i + 1], categoryId, emoji));
            else
                row.Add(MakeSpacer());

            _categoryGrid.Add(row);
        }
    }

    private Button CreateSubcategoryCard(
        SubcategoryModel sub, string categoryId, string emoji)
    {
        var card = new Button();
        card.AddToClassList("category-card");

        var iconArea = new VisualElement();
        iconArea.AddToClassList("category-card__icon-area");

        var emojiLabel = new Label(emoji);
        emojiLabel.AddToClassList("category-card__emoji");

        var nameLabel = new Label(sub.SubcategoryName);
        nameLabel.AddToClassList("category-card__name");

        iconArea.Add(emojiLabel);
        card.Add(iconArea);
        card.Add(nameLabel);

        card.RegisterCallback<ClickEvent>(evt =>
        {
            evt.StopPropagation();
            NavigateToSubcategory(sub, categoryId);
        });

        return card;
    }

    // ---------------------------------------------------------------
    // ITEM LIST
    // ---------------------------------------------------------------

    private void BuildItemList(List<ProductModel> products)
    {
        if (_body == null) return;

        BuildFilterChips();
        BuildSortBar();

        // Remove old list if exists
        _body.Q<ListView>("ProductListView")?.RemoveFromHierarchy();

        if (products == null || products.Count == 0)
        {
            var empty = new Label("No items found in this category.");
            empty.name = "ItemListScroll"; // keeps CleanItemListViews working
            empty.style.color          = new StyleColor(new Color(0.5f, 0.5f, 0.5f));
            empty.style.fontSize       = 13;
            empty.style.unityTextAlign = TextAnchor.MiddleCenter;
            empty.style.marginTop      = 40;
            empty.style.whiteSpace     = WhiteSpace.Normal;
            empty.style.paddingLeft    = 20;
            empty.style.paddingRight   = 20;
            _body.Add(empty);
            return;
        }

        string categoryEmoji = CategoryMapper.GetCategoryEmoji(_activeCategoryId);

        // makeItem — called only for visible items (typically 4-5 on screen)
        // Unity recycles these same elements as user scrolls
        VisualElement MakeItem()
        {
            var card = new VisualElement();
            card.AddToClassList("item-card");

            var imageArea = new VisualElement();
            imageArea.name = "CardImageArea";
            imageArea.AddToClassList("item-card__image-area");
            imageArea.style.backgroundColor =
                new StyleColor(new Color(0.94f, 0.94f, 0.93f));

            var emojiLabel = new Label();
            emojiLabel.name = "CardEmoji";
            emojiLabel.AddToClassList("item-card__placeholder-emoji");
            imageArea.Add(emojiLabel);

            card.Add(imageArea);

            var info = new VisualElement();
            info.AddToClassList("item-card__info");

            var textCol = new VisualElement();
            textCol.AddToClassList("item-card__text");

            var nameLabel = new Label();
            nameLabel.name = "CardName";
            nameLabel.AddToClassList("item-card__name");

            var descLabel = new Label();
            descLabel.name = "CardDesc";
            descLabel.AddToClassList("item-card__dimensions");

            textCol.Add(nameLabel);
            textCol.Add(descLabel);

            var favBtn  = new Button();
            favBtn.name = "CardFavBtn";
            favBtn.AddToClassList("item-card__fav-btn");
            var favIcon = new Label("☆");
            favIcon.name = "CardFavIcon";
            favIcon.AddToClassList("item-card__fav-icon");
            favBtn.Add(favIcon);

            info.Add(textCol);
            info.Add(favBtn);
            card.Add(info);

            return card;
        }

        // bindItem — called when a recycled card needs new data
        // Keep this fast — no async operations, just assign values
        void BindItem(VisualElement card, int index)
        {
            if (index < 0 || index >= products.Count) return;
            var product = products[index];

            var imageArea  = card.Q<VisualElement>("CardImageArea");
            var emojiLabel = card.Q<Label>("CardEmoji");
            var nameLabel  = card.Q<Label>("CardName");
            var descLabel  = card.Q<Label>("CardDesc");
            var favBtn     = card.Q<Button>("CardFavBtn");
            var favIcon    = card.Q<Label>("CardFavIcon");

            // Show "IKEA KIVIK" format
            if (nameLabel != null)
                nameLabel.text = $"IKEA {product.Name}";

            // Show variant description e.g. "2 seater sofa - Tibbleby beigegrey"
            if (descLabel != null)
            {
                string variant = product.Description ?? "";
                if (variant.StartsWith(product.Name))
                    variant = variant.Substring(product.Name.Length).Trim(' ', '-');
                descLabel.text = variant;
            }

            if (emojiLabel != null) emojiLabel.text = categoryEmoji;

            if (imageArea != null)
            {
                imageArea.style.backgroundImage = StyleKeyword.None;
                imageArea.style.backgroundColor =
                    new StyleColor(new Color(0.94f, 0.94f, 0.93f));
            }

            if (emojiLabel != null)
                emojiLabel.style.display = DisplayStyle.Flex;

            if (favIcon != null) favIcon.text = "☆";
            favBtn?.SetEnabled(true);

            if (!string.IsNullOrEmpty(product.BestImageUrl) && imageArea != null)
                LoadImageIntoElement(imageArea, emojiLabel, product.BestImageUrl);

            card.UnregisterCallback<ClickEvent>(OnCardClicked);
            card.userData = product;
            card.RegisterCallback<ClickEvent>(OnCardClicked);

            favBtn?.UnregisterCallback<ClickEvent>(OnFavClicked);
            favBtn?.RegisterCallback<ClickEvent>(OnFavClicked);
        }

        var listView = new ListView(products, 240, MakeItem, BindItem)
        {
            name                       = "ProductListView",
            selectionType              = SelectionType.None,
            virtualizationMethod       = CollectionVirtualizationMethod.FixedHeight,
            showAlternatingRowBackgrounds = AlternatingRowBackground.None,
            showBorder                 = false,
            reorderable                = false,
        };

        listView.style.flexGrow  = 1;
        listView.style.minHeight = 200;

        // Hide Unity's default item focus ring
        var listScrollView = listView.Q<ScrollView>();
        if (listScrollView != null)
            listScrollView.verticalScrollerVisibility = ScrollerVisibility.Hidden;

        _body.Add(listView);

        // Preload images for visible items only
        if (products.Count > 0)
            _ = PreloadImages(products.GetRange(0, Mathf.Min(5, products.Count)));
    }

    private void BuildFilterChips()
    {
        if (_body == null || _cachedCategories == null) return;

        var siblings = GetSiblingSubcategories(
            _activeCategoryId, _activeSubcategoryId);

        if (siblings == null || siblings.Count <= 1) return;

        var chipsScroll = new ScrollView(ScrollViewMode.Horizontal);
        chipsScroll.name = "FilterChipsScroll";
        chipsScroll.AddToClassList("filter-chips-scroll");
        chipsScroll.verticalScrollerVisibility   = ScrollerVisibility.Hidden;
        chipsScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        chipsScroll.touchScrollBehavior =
            ScrollView.TouchScrollBehavior.Clamped;
        chipsScroll.mouseWheelScrollSize = 100f;
        chipsScroll.contentContainer.style.flexDirection = FlexDirection.Row;
        chipsScroll.contentContainer.style.alignItems    = Align.Center;
        chipsScroll.contentContainer.pickingMode         = PickingMode.Position;

        foreach (var (sub, catId) in siblings)
        {
            var chipBtn = new Button();
            chipBtn.AddToClassList("filter-chip");

            if (sub.SubcategoryId == _activeSubcategoryId)
                chipBtn.AddToClassList("filter-chip--active");

            var chipLabel = new Label(sub.SubcategoryName);
            chipLabel.AddToClassList("filter-chip__label");
            chipBtn.Add(chipLabel);

            var capturedSub   = sub;
            var capturedCatId = catId;

            chipBtn.RegisterCallback<ClickEvent>(evt =>
            {
                evt.StopPropagation();

                foreach (var c in chipsScroll.contentContainer.Children())
                    c.RemoveFromClassList("filter-chip--active");
                chipBtn.AddToClassList("filter-chip--active");

                if (_navStack.Count > 0)
                {
                    var top = _navStack.Peek();
                    top.Title         = capturedSub.SubcategoryName;
                    top.SubcategoryId = capturedSub.SubcategoryId;
                }

                _activeSubcategoryId = capturedSub.SubcategoryId;
                UpdateHeader(capturedSub.SubcategoryName);
                CleanItemListViews();
                HideCategoryScroll();

                _ = FurnitureDataService.Instance
                    .LoadProductsBySubcategory(capturedCatId,
                                               capturedSub.SubcategoryId);
            });

            chipsScroll.Add(chipBtn);
        }

        _body.Add(chipsScroll);
    }

    private void BuildSortBar()
    {
        if (_body == null) return;

        var sortBar = new VisualElement();
        sortBar.name = "SortBar";
        sortBar.AddToClassList("sort-bar");

        foreach (var (label, isActive) in new[] { ("Popular", true), ("New", false) })
        {
            var btn = new Button();
            btn.AddToClassList("sort-btn");
            if (isActive) btn.AddToClassList("sort-btn--active");

            var lbl = new Label(label);
            lbl.AddToClassList("sort-btn__label");
            btn.Add(lbl);

            btn.RegisterCallback<ClickEvent>(evt =>
            {
                foreach (var c in sortBar.Children())
                    c.RemoveFromClassList("sort-btn--active");
                btn.AddToClassList("sort-btn--active");
            });

            sortBar.Add(btn);
        }

        var filterIconBtn = new Button();
        filterIconBtn.AddToClassList("sort-filter-icon-btn");
        var filterIcon = new Label("⚙");
        filterIcon.AddToClassList("sort-filter-icon-btn__label");
        filterIconBtn.Add(filterIcon);
        sortBar.Add(filterIconBtn);

        _body.Add(sortBar);
    }

    private void OnCardClicked(ClickEvent evt)
    {
        var card = evt.currentTarget as VisualElement;
        if (card?.userData is not ProductModel product) return;

        evt.StopPropagation();

        string emoji = CategoryMapper.GetCategoryEmoji(_activeCategoryId);

        // Extract variant part for display in detail sheet
        string variant = product.Description ?? "";
        if (variant.StartsWith(product.Name))
            variant = variant.Substring(product.Name.Length).Trim(' ', '-');

        Debug.Log($"[FurniturePanel] Item tapped: {product.Name}");
        OnItemSelected?.Invoke(
            $"{emoji}|{product.ProductId}|{product.S3ModelUrl}|" +
            $"{product.Name}|{variant}|{product.BestImageUrl}");
    }

    private void OnFavClicked(ClickEvent evt)
    {
        evt.StopPropagation();
        var favBtn  = evt.currentTarget as Button;
        var favIcon = favBtn?.Q<Label>("CardFavIcon");
        if (favIcon != null)
            favIcon.text = favIcon.text == "☆" ? "★" : "☆";
    }
    
    

    // ---------------------------------------------------------------
    // SIBLING SUBCATEGORIES FOR FILTER CHIPS
    // ---------------------------------------------------------------

    private List<(SubcategoryModel sub, string categoryId)>
        GetSiblingSubcategories(string categoryId, string subcategoryId)
    {
        if (_cachedCategories == null || string.IsNullOrEmpty(subcategoryId))
            return null;

        foreach (var cat in _cachedCategories)
        {
            if (cat.CategoryId != categoryId) continue;
            if (cat.Subcategories == null) continue;

            var result = new List<(SubcategoryModel, string)>();
            foreach (var sub in cat.Subcategories)
                result.Add((sub, cat.CategoryId));
            return result;
        }

        return null;
    }

    // ---------------------------------------------------------------
    // STATE MESSAGES
    // ---------------------------------------------------------------

    private void ShowLoadingState(string message)
    {
        if (_categoryGrid == null) return;
        _categoryGrid.Clear();

        var label = new Label(
            string.IsNullOrEmpty(message) ? "Loading..." : message);
        label.style.color          = new StyleColor(new Color(0.5f, 0.5f, 0.5f));
        label.style.fontSize       = 13;
        label.style.unityTextAlign = TextAnchor.MiddleCenter;
        label.style.marginTop      = 40;
        label.style.whiteSpace     = WhiteSpace.Normal;
        label.style.paddingLeft    = 20;
        label.style.paddingRight   = 20;
        _categoryGrid.Add(label);
        ShowCategoryScroll();
    }

    private void ShowErrorState(string message)
    {
        if (_categoryGrid == null) return;
        _categoryGrid.Clear();
        CleanItemListViews();
        ShowCategoryScroll();

        var label = new Label(message);
        label.style.color          = new StyleColor(new Color(0.8f, 0.2f, 0.2f));
        label.style.fontSize       = 13;
        label.style.unityTextAlign = TextAnchor.MiddleCenter;
        label.style.marginTop      = 40;
        label.style.whiteSpace     = WhiteSpace.Normal;
        label.style.paddingLeft    = 20;
        label.style.paddingRight   = 20;
        _categoryGrid.Add(label);

        var retryBtn = new Button();
        retryBtn.text = "Retry";
        retryBtn.style.marginTop  = 16;
        retryBtn.style.alignSelf  = Align.Center;
        retryBtn.style.paddingLeft = 20;
        retryBtn.style.paddingRight = 20;
        retryBtn.RegisterCallback<ClickEvent>(evt =>
        {
            evt.StopPropagation();
            _ = FurnitureDataService.Instance.LoadCategories();
        });
        _categoryGrid.Add(retryBtn);
    }

    private void ShowEmptyState(string message)
    {
        if (_categoryGrid == null) return;

        var label = new Label(message);
        label.style.color          = new StyleColor(new Color(0.5f, 0.5f, 0.5f));
        label.style.fontSize       = 13;
        label.style.unityTextAlign = TextAnchor.MiddleCenter;
        label.style.marginTop      = 40;
        label.style.whiteSpace     = WhiteSpace.Normal;
        label.style.paddingLeft    = 20;
        label.style.paddingRight   = 20;
        _categoryGrid.Add(label);
    }

    // ---------------------------------------------------------------
    // SCROLL VIEW HELPERS
    // ---------------------------------------------------------------

    private void ShowCategoryScroll()
    {
        var scroll = _panel?.Q<ScrollView>("FurniturePanelScroll");
        if (scroll != null)
            scroll.style.display = DisplayStyle.Flex;
    }

    private void HideCategoryScroll()
    {
        var scroll = _panel?.Q<ScrollView>("FurniturePanelScroll");
        if (scroll != null)
            scroll.style.display = DisplayStyle.None;
    }

    private void CleanItemListViews()
    {
        if (_body == null) return;
        _body.Q<ListView>("ProductListView")?.RemoveFromHierarchy();
        _body.Q<ScrollView>("ItemListScroll")?.RemoveFromHierarchy();
        _body.Q<ScrollView>("FilterChipsScroll")?.RemoveFromHierarchy();
        _body.Q<VisualElement>("SortBar")?.RemoveFromHierarchy();
        _body.Q<Label>("ItemListScroll")?.RemoveFromHierarchy();
    }

    private VisualElement MakeSpacer()
    {
        var spacer = new VisualElement();
        spacer.style.flexGrow   = 1;
        spacer.style.flexShrink = 1;
        spacer.style.marginLeft  = 4;
        spacer.style.marginRight = 4;
        return spacer;
    }

    // ---------------------------------------------------------------
    // OPEN / CLOSE / RESTORE
    // ---------------------------------------------------------------

    public void Open()
    {
        if (_isOpen) return;
        _isOpen = true;

        if (!_hasStoredState)
        {
            _navStack.Clear();
            UpdateHeader(null);
            CleanItemListViews();
            ShowCategoryScroll();

            if (_cachedCategories != null)
                BuildCategoryGrid(FilterCategoriesForTab(_cachedCategories));
            else
                _ = FurnitureDataService.Instance.LoadCategories();
        }
        else
        {
            _hasStoredState = false;
            RestoreState();
        }

        if (_animationCoroutine != null)
            StopCoroutine(_animationCoroutine);
        _animationCoroutine = StartCoroutine(SlideIn());
    }

    public void Close()
    {
        if (!_isOpen) return;
        _isOpen         = false;
        _hasStoredState = true;

        if (_animationCoroutine != null)
            StopCoroutine(_animationCoroutine);
        _animationCoroutine = StartCoroutine(SlideOut(fireClosedEvent: true));
    }
    
    public void ResetToRoot()
    {
        _hasStoredState = false;
        _navStack.Clear();
        _cachedProducts = null;
    }

    public void HideWithoutReset()
    {
        if (!_isOpen) return;
        _isOpen         = false;
        _hasStoredState = true;

        if (_animationCoroutine != null)
            StopCoroutine(_animationCoroutine);
        _animationCoroutine = StartCoroutine(SlideOut(fireClosedEvent: false));
    }

    public bool IsOpen => _isOpen;

    private void RestoreState()
    {
        if (_navStack.Count == 0)
        {
            UpdateHeader(null);
            CleanItemListViews();
            ShowCategoryScroll();

            if (_cachedCategories != null)
                BuildCategoryGrid(FilterCategoriesForTab(_cachedCategories));
            else
                _ = FurnitureDataService.Instance.LoadCategories();
            return;
        }

        var entry = _navStack.Peek();
        UpdateHeader(entry.Title);
        _activeCategoryId    = entry.CategoryId;
        _activeSubcategoryId = entry.SubcategoryId;

        if (entry.IsItemList && _cachedProducts != null)
        {
            CleanItemListViews();
            HideCategoryScroll();
            BuildItemList(_cachedProducts);
        }
        else
        {
            CleanItemListViews();
            ShowCategoryScroll();
            var category = _cachedCategories?.Find(
                c => c.CategoryId == entry.CategoryId);
            if (category?.Subcategories != null)
                BuildSubcategoryGrid(category.Subcategories, entry.CategoryId);
        }
    }

    // ---------------------------------------------------------------
    // ANIMATION
    // ---------------------------------------------------------------

    private IEnumerator SlideIn()
    {
        float elapsed = 0f;
        while (elapsed < SLIDE_DURATION)
        {
            elapsed += Time.deltaTime;
            float eased = EaseOutCubic(Mathf.Clamp01(elapsed / SLIDE_DURATION));
            _panel.style.translate = new StyleTranslate(
                new Translate(Length.Percent(Mathf.Lerp(100f, 0f, eased)), 0));
            yield return null;
        }
        _panel.style.translate = new StyleTranslate(
            new Translate(Length.Percent(0), 0));
    }

    private IEnumerator SlideOut(bool fireClosedEvent = true)
    {
        float elapsed = 0f;
        while (elapsed < SLIDE_DURATION)
        {
            elapsed += Time.deltaTime;
            float eased = EaseInCubic(Mathf.Clamp01(elapsed / SLIDE_DURATION));
            _panel.style.translate = new StyleTranslate(
                new Translate(Length.Percent(Mathf.Lerp(0f, 100f, eased)), 0));
            yield return null;
        }
        _panel.style.translate = new StyleTranslate(
            new Translate(Length.Percent(100), 0));

        if (fireClosedEvent)
            OnPanelClosed?.Invoke();
    }
    
    private async void LoadImageIntoElement(
        VisualElement imageArea, Label emojiLabel, string imageUrl)
    {
        if (string.IsNullOrEmpty(imageUrl)) return;

        StartShimmer(imageArea);

        var texture = await ImageCache.GetTexture(imageUrl);

        StopShimmer(imageArea);

        if (texture == null) return;

        // Guard — element may have been recycled by the time image loads
        if (imageArea.panel == null) return;

        imageArea.style.backgroundImage = new StyleBackground(texture);
        imageArea.style.backgroundSize  = new StyleBackgroundSize(
            new BackgroundSize(BackgroundSizeType.Contain));
        imageArea.style.backgroundPositionX = new StyleBackgroundPosition(
            new BackgroundPosition(BackgroundPositionKeyword.Center));
        imageArea.style.backgroundPositionY = new StyleBackgroundPosition(
            new BackgroundPosition(BackgroundPositionKeyword.Center));

        if (emojiLabel != null)
            emojiLabel.style.display = DisplayStyle.None;
    }
    
    private async Task PreloadImages(List<ProductModel> products)
    {
        foreach (var product in products)
        {
            if (!string.IsNullOrEmpty(product.BestImageUrl))
                await ImageCache.GetTexture(product.BestImageUrl);
        }
    }

    private void StartShimmer(VisualElement element)
    {
        _shimmeringElements[element] = true;
        StartCoroutine(ShimmerCoroutine(element));
    }

    private void StopShimmer(VisualElement element)
    {
        if (_shimmeringElements.ContainsKey(element))
            _shimmeringElements[element] = false;
    }

    private IEnumerator ShimmerCoroutine(VisualElement element)
    {
        Color light = new Color(0.94f, 0.94f, 0.93f);
        Color dark  = new Color(0.86f, 0.86f, 0.85f);
        float speed = 1.2f;
        float t     = 0f;
        bool  ping  = true;

        while (_shimmeringElements.TryGetValue(element, out bool active) && active)
        {
            t += Time.deltaTime * speed;
            if (t >= 1f) { t = 0f; ping = !ping; }

            float lerp = ping ? t : 1f - t;
            element.style.backgroundColor = new StyleColor(Color.Lerp(light, dark, lerp));
            yield return null;
        }

        // Reset to base color when done
        element.style.backgroundColor = new StyleColor(new Color(0.97f, 0.97f, 0.96f));
        _shimmeringElements.Remove(element);
    }
    
    private async Task PrefetchSubcategoryProducts(CategoryModel category)
    {
        if (category.Subcategories == null || category.Subcategories.Count == 0)
            return;

        Debug.Log($"[Prefetch] Starting parallel prefetch for " +
                  $"{category.CategoryName} — {category.Subcategories.Count} subcategories");

        // Fire ALL subcategory queries simultaneously instead of one by one
        var tasks = new List<Task>();
        foreach (var sub in category.Subcategories)
        {
            tasks.Add(FurnitureRepository.Instance.GetProductsBySubcategory(
                category.CategoryId, sub.SubcategoryId));
        }

        await Task.WhenAll(tasks);

        Debug.Log($"[Prefetch] All done for {category.CategoryName}");
    }

    private float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);
    private float EaseInCubic(float t)  => t * t * t;
}