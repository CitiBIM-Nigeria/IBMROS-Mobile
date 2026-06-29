using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class ItemDetailSheetController : MonoBehaviour
{
    public event Action<string>         OnAddToRoomClicked;
    public event Action                 OnSheetClosed;
    public event Action<ProductVariant> OnColorSelected;

    // ---------------------------------------------------------------
    // UI REFERENCES
    // ---------------------------------------------------------------

    private VisualElement _overlay;
    private VisualElement _sheet;
    private VisualElement _imageArea;
    private VisualElement _colorRow;
    private Button        _backButton;
    private Button        _closeButton;
    private Button        _favButton;
    private Button        _addButton;
    private Label         _emojiLabel;
    private Label         _brandLabel;
    private Label         _nameLabel;
    private Label         _dimensionsLabel;
    private Label         _descriptionLabel;
    private Label         _favIcon;

    // ---------------------------------------------------------------
    // STATE
    // ---------------------------------------------------------------

    private bool      _isOpen = false;
    private Coroutine _animCoroutine;
    private string    _currentItemKey;
    private const float SLIDE_DURATION = 0.28f;

    // ---------------------------------------------------------------
    // INITIALIZE
    // ---------------------------------------------------------------

    public void Initialize(VisualElement root)
    {
        _overlay          = root.Q<VisualElement>("ItemDetailOverlay");
        _sheet            = root.Q<VisualElement>("ItemDetailSheet");
        _imageArea        = root.Q<VisualElement>("ItemDetailImageArea");
        _colorRow         = root.Q<VisualElement>("ItemDetailColorRow");
        _backButton       = root.Q<Button>("ItemDetailBackButton");
        _closeButton      = root.Q<Button>("ItemDetailCloseButton");
        _favButton        = root.Q<Button>("ItemDetailFavBtn");
        _addButton        = root.Q<Button>("ItemDetailAddButton");
        _emojiLabel       = root.Q<Label>("ItemDetailEmoji");
        _brandLabel       = root.Q<Label>("ItemDetailBrand");
        _nameLabel        = root.Q<Label>("ItemDetailName");
        _dimensionsLabel  = root.Q<Label>("ItemDetailDimensions");
        _descriptionLabel = root.Q<Label>("ItemDetailDescription");
        _favIcon          = root.Q<Label>("ItemDetailFavIcon");

        if (_overlay == null)
            Debug.LogError("[ItemDetailSheetController] ItemDetailOverlay not found.");

        _backButton?.RegisterCallback<ClickEvent>(evt =>
        {
            evt.StopPropagation();
            Close();
        });

        _closeButton?.RegisterCallback<ClickEvent>(evt =>
        {
            evt.StopPropagation();
            Close();
        });

        _favButton?.RegisterCallback<ClickEvent>(evt =>
        {
            evt.StopPropagation();
            if (_favIcon != null)
                _favIcon.text = _favIcon.text == "☆" ? "★" : "☆";
        });

        _addButton?.RegisterCallback<ClickEvent>(evt =>
        {
            evt.StopPropagation();
            Debug.Log($"[ItemDetail] Add to Room: {_currentItemKey}");
            OnAddToRoomClicked?.Invoke(_currentItemKey);
            Close();
        });

        _overlay?.RegisterCallback<ClickEvent>(evt =>
        {
            if (evt.target == _overlay)
                Close();
        });

        _sheet?.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
        _sheet?.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
    }

    // ---------------------------------------------------------------
    // OPEN
    // ---------------------------------------------------------------

    public void Open(string emoji, string name, string description,
        string productId, string imageUrl = "",
        List<ProductVariant> variants = null)
    {
        _currentItemKey = productId;

        if (_emojiLabel      != null) _emojiLabel.text      = emoji;
        if (_brandLabel      != null) _brandLabel.text      = "IKEA";
        if (_nameLabel       != null) _nameLabel.text       = name;
        if (_favIcon         != null) _favIcon.text         = "☆";

        // description here is already the variant part
        // e.g. "2 seater sofa - Tibbleby beigegrey"
        if (_dimensionsLabel != null)
            _dimensionsLabel.text = string.IsNullOrEmpty(description) ? "" : description;

        if (_descriptionLabel != null)
            _descriptionLabel.text = string.IsNullOrEmpty(description)
                ? "A quality furniture piece designed for comfort and durability."
                : $"IKEA {name} {description}";

        if (_imageArea != null)
        {
            _imageArea.style.backgroundImage = StyleKeyword.None;
            _imageArea.style.backgroundColor =
                new StyleColor(new Color(0.97f, 0.97f, 0.96f));
        }

        if (_emojiLabel != null)
            _emojiLabel.style.display = DisplayStyle.Flex;

        if (!string.IsNullOrEmpty(imageUrl) && _imageArea != null)
            LoadImageIntoArea(_imageArea, _emojiLabel, imageUrl);

        BuildColorSwatches(variants);

        _overlay.style.display = DisplayStyle.Flex;
        _isOpen = true;

        if (_animCoroutine != null) StopCoroutine(_animCoroutine);
        _animCoroutine = StartCoroutine(SlideIn());
    }

    public void Close()
    {
        if (!_isOpen) return;
        _isOpen = false;

        if (_animCoroutine != null) StopCoroutine(_animCoroutine);
        _animCoroutine = StartCoroutine(SlideOut());
    }

    public bool IsOpen => _isOpen;

    // ---------------------------------------------------------------
    // IMAGE LOADING
    // ---------------------------------------------------------------

    private async void LoadImageIntoArea(
        VisualElement imageArea, Label emojiLabel, string imageUrl)
    {
        if (string.IsNullOrEmpty(imageUrl)) return;

        var texture = await ImageCache.GetTexture(imageUrl);
        if (texture == null) return;

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

    // ---------------------------------------------------------------
    // COLOUR SWATCHES
    // Single neutral swatch shown by default since we have no
    // variant data yet. Extend this once variant data is available.
    // ---------------------------------------------------------------

    private void BuildColorSwatches(List<ProductVariant> variants)
    {
        if (_colorRow == null) return;
        _colorRow.Clear();
        if (variants == null || variants.Count == 0) return;

        foreach (var v in variants)
        {
            // Only offer colours that actually have their own 3D model (the
            // primary always does). IKEA only publishes models for some colours,
            // so this keeps every swatch tap working instead of silently falling
            // back to the default colour.
            if (!v.IsPrimary && string.IsNullOrEmpty(v.ModelUrl))
                continue;

            var swatch = new VisualElement();
            swatch.AddToClassList("item-detail-color-swatch");
            if (v.IsPrimary)
                swatch.AddToClassList("item-detail-color-swatch--selected");

            // Prefer IKEA's fabric chip; fall back to the flat dominant colour.
            if (!string.IsNullOrEmpty(v.SwatchUrl))
                LoadSwatchImage(swatch, v.SwatchUrl);
            else if (ColorUtility.TryParseHtmlString(
                         string.IsNullOrEmpty(v.DominantColor) ? "#cccccc"
                                                               : v.DominantColor,
                         out var col))
                swatch.style.backgroundColor = new StyleColor(col);

            var captured = v;
            swatch.RegisterCallback<ClickEvent>(evt =>
            {
                evt.StopPropagation();
                SelectSwatch(swatch, captured);
            });

            _colorRow.Add(swatch);
        }
    }

    private async void LoadSwatchImage(VisualElement swatch, string url)
    {
        var tex = await ImageCache.GetTexture(url);
        if (tex != null)
            swatch.style.backgroundImage = new StyleBackground(tex);
    }

    private void SelectSwatch(VisualElement swatch, ProductVariant variant)
    {
        // Highlight the chosen swatch.
        foreach (var child in _colorRow.Children())
            child.RemoveFromClassList("item-detail-color-swatch--selected");
        swatch.AddToClassList("item-detail-color-swatch--selected");

        // Update the big preview image to this colour.
        string img = !string.IsNullOrEmpty(variant.DisplayUrl)
            ? variant.DisplayUrl : variant.DisplayOriginalUrl;
        if (!string.IsNullOrEmpty(img) && _imageArea != null)
            LoadImageIntoArea(_imageArea, _emojiLabel, img);

        OnColorSelected?.Invoke(variant);
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
            float eased = 1f - Mathf.Pow(
                1f - Mathf.Clamp01(elapsed / SLIDE_DURATION), 3f);
            _sheet.style.translate = new StyleTranslate(
                new Translate(0, Length.Percent(Mathf.Lerp(100f, 0f, eased))));
            yield return null;
        }
        _sheet.style.translate = new StyleTranslate(
            new Translate(0, Length.Percent(0)));
    }

    private IEnumerator SlideOut()
    {
        float elapsed = 0f;
        while (elapsed < SLIDE_DURATION)
        {
            elapsed += Time.deltaTime;
            float t     = Mathf.Clamp01(elapsed / SLIDE_DURATION);
            float eased = t * t * t;
            _sheet.style.translate = new StyleTranslate(
                new Translate(0, Length.Percent(Mathf.Lerp(0f, 100f, eased))));
            yield return null;
        }
        _sheet.style.translate = new StyleTranslate(
            new Translate(0, Length.Percent(100)));
        _overlay.style.display = DisplayStyle.None;
        OnSheetClosed?.Invoke();
    }
}