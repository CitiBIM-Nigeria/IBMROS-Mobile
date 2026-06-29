using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

public class ScreenNavigator : MonoBehaviour, INavigator
{
    public static ScreenNavigator Instance { get; private set; }

    // Tracks which screen is currently visible
    private ScreenName _currentScreen = ScreenName.Splash;
    public ScreenName CurrentScreen => _currentScreen;

    // Fired whenever the screen changes
    public static event Action<ScreenName> OnScreenChanged;

    // Animation duration in milliseconds
    private const int TransitionDurationMs = 200;

    // Guards against overlapping async navigations leaving two screens active
    // (the "two panels on screen at once" bug).
    private bool _isTransitioning = false;

    public bool HasBeenNavigated { get; private set; } = false;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }
    
    
    void Start()
    {
        // SceneEntryPoint already navigated, do not overwrite
        if (HasBeenNavigated)
            return;

        NavigateTo(ScreenName.Splash);
    }
    // Removes the active class from EVERY screen so only the one we explicitly
    // show stays visible. Class-based (not inline display) so it stays consistent
    // with ShowScreen/HideScreen — inline display would override the USS classes.
    private void HideAllScreens()
    {
        foreach (ScreenName screen in System.Enum.GetValues(typeof(ScreenName)))
        {
            var container = UIManager.Instance?.GetScreenContainer(screen);
            if (container == null)
                continue;
            container.RemoveFromClassList("screen--active");
            container.RemoveFromClassList("screen-auth--active");
        }
    }
    // Navigates to a screen with a fade transition
    public async void NavigateTo(ScreenName targetScreen)
    {
        if (targetScreen == _currentScreen)
            return;

        // Ignore a new navigation while one is mid-transition — overlapping
        // transitions are what left two panels active at once.
        if (_isTransitioning)
            return;

        VisualElement currentContainer = UIManager.Instance
            .GetScreenContainer(_currentScreen);

        VisualElement targetContainer = UIManager.Instance
            .GetScreenContainer(targetScreen);

        if (currentContainer == null || targetContainer == null)
        {
            Debug.LogError($"[ScreenNavigator] Could not find containers for " +
                           $"{_currentScreen} or {targetScreen}");
            return;
        }

        _isTransitioning = true;
        try
        {
            await FadeOut(currentContainer);

            // Definitively hide ALL screens, then show only the target.
            HideAllScreens();
            ShowScreen(targetContainer, targetScreen);

            await FadeIn(targetContainer);

            _currentScreen = targetScreen;
            OnScreenChanged?.Invoke(_currentScreen);
            Debug.Log($"[ScreenNavigator] Navigated to {targetScreen}");
        }
        finally
        {
            _isTransitioning = false;
        }
    }

    public void NavigateToImmediate(ScreenName targetScreen)
    {
        HasBeenNavigated = true;

        VisualElement currentContainer = UIManager.Instance
            .GetScreenContainer(_currentScreen);

        VisualElement targetContainer = UIManager.Instance
            .GetScreenContainer(targetScreen);

        if (currentContainer == null || targetContainer == null)
            return;

        HideAllScreens();
        ShowScreen(targetContainer, targetScreen);

        // Force full opacity to override any leftover fade state or CSS transition
        targetContainer.style.opacity = 1f;

        _currentScreen = targetScreen;
        OnScreenChanged?.Invoke(_currentScreen);

        Debug.Log($"[ScreenNavigator] NavigatedImmediate to {targetScreen}");
    }

    private void ShowScreen(VisualElement container, ScreenName screen)
    {
        if (screen == ScreenName.MainApp || screen == ScreenName.Splash)
            container.AddToClassList("screen--active");
        else
            container.AddToClassList("screen-auth--active");
    }

    private void HideScreen(VisualElement container, ScreenName screen)
    {
        if (screen == ScreenName.MainApp || screen == ScreenName.Splash)
            container.RemoveFromClassList("screen--active");
        else
            container.RemoveFromClassList("screen-auth--active");
    }

    private async Task FadeOut(VisualElement element)
    {
        try
        {
            // Fade from full opacity to transparent
            for (float t = 1f; t >= 0f; t -= 0.1f)
            {
                element.style.opacity = t;
                await Task.Delay(TransitionDurationMs / 10);
            }
            element.style.opacity = 0f;
        }
        catch (Exception)
        {
            element.style.opacity = 0f;
        }
    }

    private async Task FadeIn(VisualElement element)
    {
        try
        {
            element.style.opacity = 0f;

            // Fade from transparent to full opacity
            for (float t = 0f; t <= 1f; t += 0.1f)
            {
                element.style.opacity = t;
                await Task.Delay(TransitionDurationMs / 10);
            }
            element.style.opacity = 1f;
        }
        catch (Exception)
        {
            element.style.opacity = 1f;
        }
    }
}