using System;
using System.Threading;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;

public class MainAppController : MonoBehaviour, IAuthUI
{
    // ---------------------------------------------------------------
    // UI ELEMENTS
    // ---------------------------------------------------------------

    // Header
    private Label         _greetingText;
    private Label         _userNameText;
    private Button        _notificationButton;
    private Button        _profileButton;
    private Label         _profileInitials;

    // No connection banner
    private VisualElement _noConnectionBanner;
    private Button        _retryConnectionButton;

    // Profile panel
    private VisualElement _profilePanel;
    private VisualElement _profilePanelBackdrop;
    private Label         _profilePanelInitials;
    private Label         _profilePanelName;
    private Label         _profilePanelEmail;
    private Button        _logoutButton;
    private Label         _logoutLabel;
    private Button        _deleteAccountButton;
    private Button        _profileSettingsButton;

    // Theme toggle (pill track inside profile panel)
    private VisualElement _themeToggleTrack;

    // Accent colour dots
    private Button _accentDotBlue;
    private Button _accentDotGreen;
    private Button _accentDotPurple;
    private Button _accentDotAmber;
    private Button _accentDotRose;

    // Bottom navigation — 3 items
    private Button _navHomeButton;
    private Button _navRoomsButton;
    private Button _navProfileButton;

    // Rooms
    private Button        _addRoomButton;
    private VisualElement _roomsEmptyHint;

    // Loading overlay
    private VisualElement _loadingOverlay;
    private Label         _loadingText;

    // Theme & accent — root container reference
    private VisualElement _rootContainer;

    // ---------------------------------------------------------------
    // STATE
    // ---------------------------------------------------------------

    private bool   _isDarkTheme    = false;   // light is default
    private string _currentAccent  = "blue";  // matches accent-dot--blue

    private bool                    _profilePanelOpen = false;
    private CancellationTokenSource _feedbackCts;

    // PlayerPrefs keys
    private const string PrefTheme  = "ibmros_theme_dark";
    private const string PrefAccent = "ibmros_accent";

    // ---------------------------------------------------------------
    // LIFECYCLE
    // ---------------------------------------------------------------

    void Start()
    {
        AuthService.OnLogoutSuccess           += OnLogoutSuccess;
        AuthService.OnDeleteAccountSuccess    += OnDeleteAccountSuccess;
        AuthService.OnSessionExpiredOrInvalid += OnSessionExpiredOrInvalid;
        AuthService.OnNetworkLost             += OnNetworkLost;
        AuthService.OnNetworkRestored         += OnNetworkRestored;
        AuthService.OnLoadingChanged          += OnLoadingStateChanged;
        ScreenNavigator.OnScreenChanged       += OnScreenChanged;
    }

    void OnDestroy()
    {
        AuthService.OnLogoutSuccess           -= OnLogoutSuccess;
        AuthService.OnDeleteAccountSuccess    -= OnDeleteAccountSuccess;
        AuthService.OnSessionExpiredOrInvalid -= OnSessionExpiredOrInvalid;
        AuthService.OnNetworkLost             -= OnNetworkLost;
        AuthService.OnNetworkRestored         -= OnNetworkRestored;
        AuthService.OnLoadingChanged          -= OnLoadingStateChanged;
        ScreenNavigator.OnScreenChanged       -= OnScreenChanged;
        UIManager.OnScreensReady              -= OnScreensReady;
        _feedbackCts?.Cancel();
        _feedbackCts?.Dispose();
    }

    void OnEnable()
    {
        if (UIManager.Instance != null && UIManager.Instance.IsReady)
        {
            QueryElements();
            WireEvents();
            LoadSavedPreferences();
            PopulateUserInfo();
            ShowRoomsEmptyHint(true);   // no saved rooms yet
            if (SessionRefreshService.Instance != null)
                SessionRefreshService.Instance.Begin();
        }
        else
        {
            UIManager.OnScreensReady += OnScreensReady;
        }
    }

    void OnDisable()
    {
        UIManager.OnScreensReady -= OnScreensReady;
        SessionRefreshService.Instance?.Stop();
        CloseProfilePanel();
    }

    private void OnScreensReady()
    {
        UIManager.OnScreensReady -= OnScreensReady;
        QueryElements();
        WireEvents();
        LoadSavedPreferences();
        PopulateUserInfo();
        ShowRoomsEmptyHint(true);
        SessionRefreshService.Instance?.Begin();
    }

    public void OnScreenActivated() => PopulateUserInfo();

    // ---------------------------------------------------------------
    // QUERY ELEMENTS
    // ---------------------------------------------------------------

    private void QueryElements()
    {
        var container = UIManager.Instance.GetScreenContainer(ScreenName.MainApp);
        if (container == null)
        {
            Debug.LogError("[MainAppController] MainApp container not found.");
            return;
        }

        _rootContainer = container;

        // The home content fits one screen, so it must not be touch-draggable.
        // The UXML flag wasn't taking effect at runtime, so force it here.
        var mainScroll = container.Q<ScrollView>("MainScroll");
        if (mainScroll != null)
        {
            mainScroll.mode                       = ScrollViewMode.Vertical;
            mainScroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            mainScroll.touchScrollBehavior        = ScrollView.TouchScrollBehavior.Clamped;
            mainScroll.elasticity                 = 0f;
        }

        // Header
        _greetingText          = container.Q<Label>("GreetingText");
        _userNameText          = container.Q<Label>("UserNameText");
        _notificationButton    = container.Q<Button>("NotificationButton");
        _profileButton         = container.Q<Button>("ProfileButton");
        _profileInitials       = container.Q<Label>("ProfileInitials");

        // Connection banner
        _noConnectionBanner    = container.Q<VisualElement>("NoConnectionBanner");
        _retryConnectionButton = container.Q<Button>("RetryConnectionButton");

        // Profile panel
        _profilePanel          = container.Q<VisualElement>("ProfilePanel");
        _profilePanelBackdrop  = container.Q<VisualElement>("ProfilePanelBackdrop");
        _profilePanelInitials  = container.Q<Label>("ProfilePanelInitials");
        _profilePanelName      = container.Q<Label>("ProfilePanelName");
        _profilePanelEmail     = container.Q<Label>("ProfilePanelEmail");
        _logoutButton          = container.Q<Button>("LogoutButton");
        _logoutLabel           = container.Q<Label>("LogoutLabel");
        _deleteAccountButton   = container.Q<Button>("DeleteAccountButton");
        _profileSettingsButton = container.Q<Button>("ProfileSettingsButton");

        // Theme toggle + accent dots
        _themeToggleTrack  = container.Q<VisualElement>("ThemeToggleTrack");
        _accentDotBlue     = container.Q<Button>("AccentDotBlue");
        _accentDotGreen    = container.Q<Button>("AccentDotGreen");
        _accentDotPurple   = container.Q<Button>("AccentDotPurple");
        _accentDotAmber    = container.Q<Button>("AccentDotAmber");
        _accentDotRose     = container.Q<Button>("AccentDotRose");

        // Nav
        _navHomeButton    = container.Q<Button>("NavHomeButton");
        _navRoomsButton   = container.Q<Button>("NavRoomsButton");
        _navProfileButton = container.Q<Button>("NavProfileButton");

        // Rooms
        _addRoomButton   = container.Q<Button>("AddRoomButton");
        _roomsEmptyHint  = container.Q<VisualElement>("RoomsEmptyHint");

        // Loading overlay
        _loadingOverlay = container.Q<VisualElement>("LoadingOverlay");
        _loadingText    = container.Q<Label>("LoadingText");
    }

    // ---------------------------------------------------------------
    // WIRE EVENTS
    // ---------------------------------------------------------------

    private void WireEvents()
    {
        // Header
        _profileButton?.RegisterCallback<ClickEvent>(
            evt => OnProfileButtonClicked());

        // Profile panel
        _profilePanelBackdrop?.RegisterCallback<ClickEvent>(
            evt => CloseProfilePanel());

        _logoutButton?.RegisterCallback<ClickEvent>(
            evt => OnLogoutClicked());

        _deleteAccountButton?.RegisterCallback<ClickEvent>(
            evt => OnDeleteAccountClicked());

        _profileSettingsButton?.RegisterCallback<ClickEvent>(
            evt => OnSettingsClicked());

        // Theme toggle pill
        _themeToggleTrack?.RegisterCallback<ClickEvent>(
            evt => OnThemeToggleClicked());

        // Accent dots
        _accentDotBlue?.RegisterCallback<ClickEvent>(
            evt => SetAccent("blue"));

        _accentDotGreen?.RegisterCallback<ClickEvent>(
            evt => SetAccent("green"));

        _accentDotPurple?.RegisterCallback<ClickEvent>(
            evt => SetAccent("purple"));

        _accentDotAmber?.RegisterCallback<ClickEvent>(
            evt => SetAccent("amber"));

        _accentDotRose?.RegisterCallback<ClickEvent>(
            evt => SetAccent("rose"));

        // Connection banner
        _retryConnectionButton?.RegisterCallback<ClickEvent>(
            evt => OnRetryConnectionClicked());

        // Rooms
        _addRoomButton?.RegisterCallback<ClickEvent>(
            evt => OnAddRoomClicked());

        // Bottom nav
        _navHomeButton?.RegisterCallback<ClickEvent>(
            evt => SetActiveNavTab(_navHomeButton));

        _navRoomsButton?.RegisterCallback<ClickEvent>(
            evt => SetActiveNavTab(_navRoomsButton));

        _navProfileButton?.RegisterCallback<ClickEvent>(
            evt => OnProfileButtonClicked());
    }

    // ---------------------------------------------------------------
    // THEME  —  dark toggle
    // Default is light. Adding "theme-dark" switches to dark.
    // ---------------------------------------------------------------

    private void OnThemeToggleClicked()
    {
        _isDarkTheme = !_isDarkTheme;
        ApplyTheme();
        SavePreferences();
    }

    /// <summary>Programmatically set dark (true) or light (false).</summary>
    public void SetTheme(bool dark)
    {
        _isDarkTheme = dark;
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        if (_rootContainer == null) return;

        if (_isDarkTheme)
        {
            _rootContainer.AddToClassList("theme-dark");
            _themeToggleTrack?.AddToClassList("theme-toggle-track--on");
        }
        else
        {
            _rootContainer.RemoveFromClassList("theme-dark");
            _themeToggleTrack?.RemoveFromClassList("theme-toggle-track--on");
        }

        Debug.Log($"[MainAppController] Theme: {(_isDarkTheme ? "Dark" : "Light")}");
    }

    // ---------------------------------------------------------------
    // ACCENT COLOUR
    // ---------------------------------------------------------------

    /// <summary>
    /// Valid names: "blue" | "green" | "purple" | "amber" | "rose"
    /// </summary>
    public void SetAccent(string accentName)
    {
        if (_rootContainer == null) return;

        // Remove old accent class (blue has no class — it's the default)
        if (_currentAccent != "blue")
            _rootContainer.RemoveFromClassList($"accent-{_currentAccent}");

        _currentAccent = accentName;

        // Add new accent class (blue needs no class)
        if (accentName != "blue")
            _rootContainer.AddToClassList($"accent-{accentName}");

        UpdateAccentDotSelection(accentName);
        SavePreferences();

        Debug.Log($"[MainAppController] Accent: {accentName}");
    }

    private void UpdateAccentDotSelection(string accentName)
    {
        // Map dot buttons to their accent names
        (Button dot, string name)[] dots =
        {
            (_accentDotBlue,   "blue"),
            (_accentDotGreen,  "green"),
            (_accentDotPurple, "purple"),
            (_accentDotAmber,  "amber"),
            (_accentDotRose,   "rose"),
        };

        foreach (var (dot, name) in dots)
        {
            if (dot == null) continue;

            if (name == accentName)
                dot.AddToClassList("accent-dot--selected");
            else
                dot.RemoveFromClassList("accent-dot--selected");
        }
    }

    // ---------------------------------------------------------------
    // PREFERENCES  —  persist theme + accent across sessions
    // ---------------------------------------------------------------

    private void LoadSavedPreferences()
    {
        // Theme
        bool savedDark = PlayerPrefs.GetInt(PrefTheme, 0) == 1;
        _isDarkTheme = savedDark;
        ApplyTheme();

        // Accent
        string savedAccent = PlayerPrefs.GetString(PrefAccent, "blue");
        _currentAccent = "blue";                  // reset before applying
        SetAccent(savedAccent);
    }

    private void SavePreferences()
    {
        PlayerPrefs.SetInt(PrefTheme, _isDarkTheme ? 1 : 0);
        PlayerPrefs.SetString(PrefAccent, _currentAccent);
        PlayerPrefs.Save();
    }

    // ---------------------------------------------------------------
    // ROOMS EMPTY HINT
    // Call ShowRoomsEmptyHint(false) once you have saved rooms to show.
    // ---------------------------------------------------------------

    public void ShowRoomsEmptyHint(bool show)
    {
        if (_roomsEmptyHint == null) return;

        _roomsEmptyHint.style.display = show
            ? DisplayStyle.Flex
            : DisplayStyle.None;
    }

    // ---------------------------------------------------------------
    // USER INFO
    // ---------------------------------------------------------------

    private void PopulateUserInfo()
    {
        bool loggedIn = SessionManager.Instance != null
                        && SessionManager.Instance.IsLoggedIn;
        string email  = loggedIn ? (SessionManager.Instance.Email ?? string.Empty)
                                 : string.Empty;

        string greeting = GetTimeOfDayGreeting();
        string displayName, initials, panelEmail;

        if (loggedIn && email.Contains("@"))
        {
            displayName = email.Split('@')[0];
            initials    = GetInitials(email);
            panelEmail  = email;
        }
        else
        {
            // Guest — no account signed in.
            displayName = "Guest";
            initials    = "G";
            panelEmail  = "Not signed in";
        }

        if (_greetingText        != null) _greetingText.text        = greeting;
        if (_userNameText        != null) _userNameText.text        = displayName;
        if (_profileInitials     != null) _profileInitials.text     = initials;
        if (_profilePanelInitials!= null) _profilePanelInitials.text= initials;
        if (_profilePanelName    != null) _profilePanelName.text    = displayName;
        if (_profilePanelEmail   != null) _profilePanelEmail.text   = panelEmail;

        // Account action reflects state: guests get "Sign in", and "Delete
        // account" is hidden (there's no account to delete).
        if (_logoutLabel != null)
            _logoutLabel.text = loggedIn ? "Sign out" : "Sign in";
        if (_deleteAccountButton != null)
            _deleteAccountButton.style.display =
                loggedIn ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private string GetInitials(string email)
    {
        if (string.IsNullOrEmpty(email)) return "?";
        string name = email.Contains("@") ? email.Split('@')[0] : email;
        return name.Length >= 2 ? name.Substring(0, 2).ToUpper() : name.ToUpper();
    }

    private string GetTimeOfDayGreeting()
    {
        int hour = DateTime.Now.Hour;
        if (hour >= 5  && hour < 12) return "Good morning";
        if (hour >= 12 && hour < 17) return "Good afternoon";
        if (hour >= 17 && hour < 21) return "Good evening";
        return "Good night";
    }

    // ---------------------------------------------------------------
    // PROFILE PANEL
    // ---------------------------------------------------------------

    private void OnProfileButtonClicked()
    {
        if (_profilePanelOpen) CloseProfilePanel();
        else                   OpenProfilePanel();
    }

    private void OpenProfilePanel()
    {
        _profilePanel?.AddToClassList("main-profile-panel--visible");
        _profilePanelBackdrop?.AddToClassList("main-profile-backdrop--visible");
        _profilePanelOpen = true;
    }

    private void CloseProfilePanel()
    {
        _profilePanel?.RemoveFromClassList("main-profile-panel--visible");
        _profilePanelBackdrop?.RemoveFromClassList("main-profile-backdrop--visible");
        _profilePanelOpen = false;
    }

    // ---------------------------------------------------------------
    // BOTTOM NAV
    // ---------------------------------------------------------------

    private void SetActiveNavTab(Button activeTab)
    {
        Button[] tabs = { _navHomeButton, _navRoomsButton, _navProfileButton };
        foreach (var tab in tabs)
        {
            if (tab == null) continue;
            if (tab == activeTab) tab.AddToClassList("main-nav-item--active");
            else                  tab.RemoveFromClassList("main-nav-item--active");
        }
    }

    // ---------------------------------------------------------------
    // BUTTON HANDLERS
    // ---------------------------------------------------------------

    private async void OnLogoutClicked()
    {
        CloseProfilePanel();
        // The row is "Sign out" for a signed-in user and "Sign in" for a guest.
        if (SessionManager.Instance != null && SessionManager.Instance.IsLoggedIn)
            await AuthService.Instance.Logout();
        else
            ScreenNavigator.Instance.NavigateTo(ScreenName.Login);
    }

    private void OnDeleteAccountClicked()
    {
        CloseProfilePanel();
        ScreenNavigator.Instance.NavigateTo(ScreenName.DeleteAccount);
    }

    private void OnSettingsClicked()
    {
        CloseProfilePanel();
        Debug.Log("[MainAppController] Settings — coming soon.");
    }

    private void OnRetryConnectionClicked()
    {
        bool connected = Application.internetReachability
                         != NetworkReachability.NotReachable;
        if (!connected)
            Debug.Log("[MainAppController] Still no connection.");
    }

    private void OnAddRoomClicked()
    {
        Debug.Log("[MainAppController] Loading Room scene.");
        SceneManager.LoadScene("Room");
    }

    // ---------------------------------------------------------------
    // AUTHSERVICE CALLBACKS
    // ---------------------------------------------------------------

    // Guest-first: after signing out / deleting / a lost session, stay in the
    // app as a guest and just refresh the menu (→ "Sign in", no Delete).
    private void OnLogoutSuccess(string message)
    {
        ScreenNavigator.Instance.NavigateTo(ScreenName.MainApp);
        PopulateUserInfo();
    }

    private void OnDeleteAccountSuccess(string message)
    {
        ScreenNavigator.Instance.NavigateTo(ScreenName.MainApp);
        PopulateUserInfo();
    }

    private void OnSessionExpiredOrInvalid()
    {
        ScreenNavigator.Instance.NavigateTo(ScreenName.MainApp);
        PopulateUserInfo();
    }

    private void OnNetworkLost()
        => UIManager.Instance.ShowNoConnectionBanner();

    private void OnNetworkRestored()
        => UIManager.Instance.HideNoConnectionBanner();

    private void OnLoadingStateChanged(bool isLoading, string message)
        => SetLoadingState(isLoading, message);

    private void OnScreenChanged(ScreenName screen)
    {
        if (screen != ScreenName.MainApp) { CloseProfilePanel(); return; }
        PopulateUserInfo();
    }

    // ---------------------------------------------------------------
    // IAuthUI
    // ---------------------------------------------------------------

    public void ShowError(string message)
        => Debug.LogWarning($"[MainAppController] Error: {message}");

    public void ShowSuccess(string message)
        => Debug.Log($"[MainAppController] Success: {message}");

    public void ClearFeedback()
    {
        _feedbackCts?.Cancel();
        _feedbackCts = null;
    }

    public void SetLoadingState(bool isLoading, string message = "Please wait...")
    {
        if (_loadingText != null) _loadingText.text = message;
        if (isLoading) _loadingOverlay?.AddToClassList("loading-overlay--visible");
        else           _loadingOverlay?.RemoveFromClassList("loading-overlay--visible");
    }

    public void SetLoadingState(bool isLoading)
        => SetLoadingState(isLoading, "Please wait...");
}