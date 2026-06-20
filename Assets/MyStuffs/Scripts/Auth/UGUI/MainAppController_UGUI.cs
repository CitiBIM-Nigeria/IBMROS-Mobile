using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class MainAppController_UGUI : MonoBehaviour, IAuthUI
{
    [Header("Header")]
    [SerializeField] private TextMeshProUGUI greetingText;
    [SerializeField] private TextMeshProUGUI userNameText;
    [SerializeField] private Button          notificationButton;
    [SerializeField] private Button          profileButton;
    [SerializeField] private TextMeshProUGUI profileInitials;

    [Header("Profile Panel")]
    [SerializeField] private GameObject      profilePanel;
    [SerializeField] private Button          profilePanelBackdrop;
    [SerializeField] private TextMeshProUGUI profilePanelInitials;
    [SerializeField] private TextMeshProUGUI profilePanelName;
    [SerializeField] private TextMeshProUGUI profilePanelEmail;
    [SerializeField] private Button          logoutButton;
    [SerializeField] private Button          deleteAccountButton;
    [SerializeField] private Button          profileSettingsButton;

    [Header("Rooms")]
    [SerializeField] private Button addRoomButton;

    [Header("No Connection Banner")]
    [SerializeField] private GameObject noConnectionBanner;
    [SerializeField] private Button     retryConnectionButton;

    [Header("Loading")]
    [SerializeField] private GameObject      loadingOverlay;
    [SerializeField] private TextMeshProUGUI loadingText;

    private bool _profilePanelOpen = false;

    // ---------------------------------------------------------------
    // LIFECYCLE
    // ---------------------------------------------------------------

    void Start()
    {
        AuthService.OnLogoutSuccess            += OnLogoutSuccess;
        AuthService.OnDeleteAccountSuccess     += OnDeleteAccountSuccess;
        AuthService.OnSessionExpiredOrInvalid  += OnSessionExpiredOrInvalid;
        AuthService.OnNetworkLost              += OnNetworkLost;
        AuthService.OnNetworkRestored          += OnNetworkRestored;
        AuthService.OnLoadingChanged           += OnLoadingStateChanged;
        ScreenNavigator_UGUI.OnScreenChanged   += OnScreenChanged;
    }

    void OnDestroy()
    {
        AuthService.OnLogoutSuccess            -= OnLogoutSuccess;
        AuthService.OnDeleteAccountSuccess     -= OnDeleteAccountSuccess;
        AuthService.OnSessionExpiredOrInvalid  -= OnSessionExpiredOrInvalid;
        AuthService.OnNetworkLost              -= OnNetworkLost;
        AuthService.OnNetworkRestored          -= OnNetworkRestored;
        AuthService.OnLoadingChanged           -= OnLoadingStateChanged;
        ScreenNavigator_UGUI.OnScreenChanged   -= OnScreenChanged;
    }

    void OnEnable()
    {
        WireEvents();
        OnScreenActivated();
    }

    void OnDisable()
    {
        UnwireEvents();
        CloseProfilePanel();
    }

    // ---------------------------------------------------------------
    // IAuthUI
    // ---------------------------------------------------------------

    public void OnScreenActivated() => PopulateUserInfo();

    // ---------------------------------------------------------------
    // EVENT WIRING
    // ---------------------------------------------------------------

    private void WireEvents()
    {
        profileButton?.onClick.AddListener(OnProfileButtonClicked);
        profilePanelBackdrop?.onClick.AddListener(CloseProfilePanel);
        logoutButton?.onClick.AddListener(OnLogoutClicked);
        deleteAccountButton?.onClick.AddListener(OnDeleteAccountClicked);
        profileSettingsButton?.onClick.AddListener(OnSettingsClicked);
        retryConnectionButton?.onClick.AddListener(OnRetryConnectionClicked);
        addRoomButton?.onClick.AddListener(OnAddRoomClicked);
    }

    private void UnwireEvents()
    {
        profileButton?.onClick.RemoveListener(OnProfileButtonClicked);
        profilePanelBackdrop?.onClick.RemoveListener(CloseProfilePanel);
        logoutButton?.onClick.RemoveListener(OnLogoutClicked);
        deleteAccountButton?.onClick.RemoveListener(OnDeleteAccountClicked);
        profileSettingsButton?.onClick.RemoveListener(OnSettingsClicked);
        retryConnectionButton?.onClick.RemoveListener(OnRetryConnectionClicked);
        addRoomButton?.onClick.RemoveListener(OnAddRoomClicked);
    }

    // ---------------------------------------------------------------
    // USER INFO
    // ---------------------------------------------------------------

    private void PopulateUserInfo()
    {
        if (SessionManager.Instance == null) return;

        string email       = SessionManager.Instance.Email ?? string.Empty;
        string initials    = GetInitials(email);
        string greeting    = GetTimeOfDayGreeting();
        string displayName = email.Contains("@") ? email.Split('@')[0] : email;

        if (greetingText        != null) greetingText.text        = greeting;
        if (userNameText        != null) userNameText.text        = displayName;
        if (profileInitials     != null) profileInitials.text     = initials;
        if (profilePanelInitials!= null) profilePanelInitials.text= initials;
        if (profilePanelName    != null) profilePanelName.text    = displayName;
        if (profilePanelEmail   != null) profilePanelEmail.text   = email;
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
        profilePanel?.SetActive(true);
        profilePanelBackdrop?.gameObject.SetActive(true);
        _profilePanelOpen = true;
    }

    private void CloseProfilePanel()
    {
        profilePanel?.SetActive(false);
        profilePanelBackdrop?.gameObject.SetActive(false);
        _profilePanelOpen = false;
    }

    // ---------------------------------------------------------------
    // BUTTON HANDLERS
    // ---------------------------------------------------------------

    private async void OnLogoutClicked()
    {
        CloseProfilePanel();
        await AuthService.Instance.Logout();
    }

    private void OnDeleteAccountClicked()
    {
        CloseProfilePanel();
        ScreenNavigator_UGUI.Instance.NavigateTo(ScreenName.DeleteAccount);
    }

    private void OnSettingsClicked()
    {
        CloseProfilePanel();
        Debug.Log("[MainAppController_UGUI] Settings — coming soon.");
    }

    private void OnRetryConnectionClicked()
    {
        bool connected = Application.internetReachability
                         != NetworkReachability.NotReachable;
        if (!connected)
            Debug.Log("[MainAppController_UGUI] Still no connection.");
    }

    private void OnAddRoomClicked()
    {
        Debug.Log("[MainAppController] Loading Room scene.");
        if (UIManager.Instance != null)
            UIManager.Instance.gameObject.SetActive(false);
        SceneManager.LoadScene("Room");
    }

    // ---------------------------------------------------------------
    // AUTHSERVICE CALLBACKS
    // ---------------------------------------------------------------

    private void OnLogoutSuccess(string message)
        => ScreenNavigator_UGUI.Instance.NavigateTo(ScreenName.Login);

    private void OnDeleteAccountSuccess(string message)
        => ScreenNavigator_UGUI.Instance.NavigateTo(ScreenName.Login);

    private void OnSessionExpiredOrInvalid()
        => ScreenNavigator_UGUI.Instance.NavigateTo(ScreenName.Login);

    private void OnNetworkLost()
        => noConnectionBanner?.SetActive(true);

    private void OnNetworkRestored()
        => noConnectionBanner?.SetActive(false);

    private void OnLoadingStateChanged(bool isLoading, string message)
    {
        SetLoadingState(isLoading);
        if (loadingText != null) loadingText.text = message;
    }

    private void OnScreenChanged(ScreenName screen)
    {
        if (screen != ScreenName.MainApp) { CloseProfilePanel(); return; }
        PopulateUserInfo();
    }

    // ---------------------------------------------------------------
    // IAuthUI
    // ---------------------------------------------------------------

    public void ShowError(string message)
        => Debug.LogWarning($"[MainAppController_UGUI] Error: {message}");

    public void ShowSuccess(string message)
        => Debug.Log($"[MainAppController_UGUI] Success: {message}");

    public void ClearFeedback() { }

    public void SetLoadingState(bool isLoading)
        => loadingOverlay?.SetActive(isLoading);
}