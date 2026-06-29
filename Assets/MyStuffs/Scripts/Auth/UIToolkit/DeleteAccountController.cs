using System.Threading;
using UnityEngine;
using UnityEngine.UIElements;

public class DeleteAccountController : MonoBehaviour, IAuthUI
{
    // UI Elements
    private TextField _passwordInput;
    private Button _passwordToggle;
    private Button _confirmDeleteButton;
    private Button _cancelButton;
    private Button _backToMainButton;
    private Button _contactSupportButton;
    private Button _confirmCheckbox;
    private VisualElement _passwordInputRow;
    private Label _accountEmail;
    private Label _accountInitials;
    private VisualElement _accountAvatar;
    private Label _checkboxIcon;
    private Label _passwordError;
    private VisualElement _feedbackBanner;
    private Label _feedbackText;
    private VisualElement _loadingOverlay;
    private Label _loadingText;
    private CancellationTokenSource _feedbackCts;

    // State
    private bool _passwordVisible = false;
    private bool _checkboxChecked = false;

    void Start()
    {
        AuthService.OnDeleteAccountSuccess += OnDeleteAccountSuccess;
        AuthService.OnDeleteAccountFailed += OnDeleteAccountFailed;
        AuthService.OnLoadingChanged += OnLoadingStateChanged;
    }

    void OnDestroy()
    {
        AuthService.OnDeleteAccountSuccess -= OnDeleteAccountSuccess;
        AuthService.OnDeleteAccountFailed -= OnDeleteAccountFailed;
        AuthService.OnLoadingChanged -= OnLoadingStateChanged;
        _feedbackCts?.Cancel();
        _feedbackCts?.Dispose();
        UIManager.OnScreensReady -= OnScreensReady;
    }

    void OnEnable()
    {
        // Clear the password field every time this screen becomes active so it
        // never shows a previous entry.
        ScreenNavigator.OnScreenChanged -= OnScreenChanged;
        ScreenNavigator.OnScreenChanged += OnScreenChanged;

        if (UIManager.Instance != null && UIManager.Instance.IsReady)
        {
            QueryElements();
            WireEvents();
            ClearFeedback();
            OnScreenActivated();
        }
        else
        {
            UIManager.OnScreensReady += OnScreensReady;
        }
    }

    void OnDisable()
    {
        UIManager.OnScreensReady -= OnScreensReady;
        ScreenNavigator.OnScreenChanged -= OnScreenChanged;
    }

    private void OnScreenChanged(ScreenName screen)
    {
        if (screen != ScreenName.DeleteAccount)
            return;
        ClearFeedback();
        OnScreenActivated();   // clears the password field
    }

    private void OnScreensReady()
    {
        UIManager.OnScreensReady -= OnScreensReady;
        QueryElements();
        WireEvents();
        ClearFeedback();
        OnScreenActivated();
    }

    // IAuthUI implementation
    public void OnScreenActivated()
    {
        ClearInputs();
        PopulateAccountInfo();
    }

    private void QueryElements()
    {
        var container = UIManager.Instance.GetScreenContainer(ScreenName.DeleteAccount);

        if (container == null)
        {
            Debug.LogError("[DeleteAccountController] DeleteAccount container not found.");
            return;
        }

        _passwordInput = container.Q<TextField>("PasswordInput");
        _passwordToggle = container.Q<Button>("PasswordToggle");
        _confirmDeleteButton = container.Q<Button>("ConfirmDeleteButton");
        _cancelButton = container.Q<Button>("CancelButton");
        _backToMainButton = container.Q<Button>("BackToMainButton");
        _contactSupportButton = container.Q<Button>("ContactSupportButton");
        _confirmCheckbox = container.Q<Button>("ConfirmCheckbox");
        _passwordInputRow = container.Q<VisualElement>("PasswordInputRow");
        _accountEmail = container.Q<Label>("AccountEmail");
        _accountInitials = container.Q<Label>("AccountInitials");
        _accountAvatar = container.Q<VisualElement>("AccountAvatar");
        _checkboxIcon = container.Q<Label>("CheckboxIcon");
        _passwordError = container.Q<Label>("PasswordError");
        _feedbackBanner = container.Q<VisualElement>("FeedbackBanner");
        _feedbackText = container.Q<Label>("FeedbackText");
        _loadingOverlay = container.Q<VisualElement>("LoadingOverlay");
        _loadingText = container.Q<Label>("LoadingText");
    }

    private void WireEvents()
    {
        if (_confirmDeleteButton != null)
            _confirmDeleteButton.clicked += OnConfirmDeleteClicked;

        if (_cancelButton != null)
            _cancelButton.clicked += OnCancelClicked;

        if (_backToMainButton != null)
            _backToMainButton.clicked += OnCancelClicked;

        if (_passwordToggle != null)
            _passwordToggle.clicked += OnPasswordToggleClicked;

        if (_confirmCheckbox != null)
            _confirmCheckbox.clicked += OnCheckboxClicked;

        if (_contactSupportButton != null)
            _contactSupportButton.clicked += OnContactSupportClicked;

        if (_passwordInput != null)
        {
            _passwordInput.RegisterCallback<FocusInEvent>(evt =>
                _passwordInputRow?.AddToClassList("input-password-row--focused"));

            _passwordInput.RegisterCallback<FocusOutEvent>(evt =>
                _passwordInputRow?.RemoveFromClassList("input-password-row--focused"));
        }
    }

    private void PopulateAccountInfo()
    {
        if (SessionManager.Instance == null)
            return;

        string email = SessionManager.Instance.Email ?? string.Empty;

        if (_accountEmail != null)
            _accountEmail.text = email;

        if (_accountInitials != null)
            _accountInitials.text = GetInitials(email);

        UpdateDeleteButtonState();
    }

    private string GetInitials(string email)
    {
        if (string.IsNullOrEmpty(email))
            return "?";

        string name = email.Contains("@") ? email.Split('@')[0] : email;
        return name.Length >= 2
            ? name.Substring(0, 2).ToUpper()
            : name.ToUpper();
    }

    private void OnCheckboxClicked()
    {
        _checkboxChecked = !_checkboxChecked;

        if (_confirmCheckbox == null || _checkboxIcon == null)
            return;

        if (_checkboxChecked)
        {
            _confirmCheckbox.AddToClassList("delete-account-checkbox--checked");
            _checkboxIcon.text = "\u2713";
        }
        else
        {
            _confirmCheckbox.RemoveFromClassList("delete-account-checkbox--checked");
            _checkboxIcon.text = string.Empty;
        }

        UpdateDeleteButtonState();
    }

    private void UpdateDeleteButtonState()
    {
        _confirmDeleteButton?.SetEnabled(_checkboxChecked);
    }

    // BUTTON HANDLERS

    private async void OnConfirmDeleteClicked()
    {
        ClearFeedback();

        if (!_checkboxChecked)
        {
            ShowError("Please confirm you understand this action is permanent.");
            return;
        }

        string password = _passwordInput?.value;

        if (string.IsNullOrWhiteSpace(password))
        {
            ShowPasswordError("Password is required to delete your account.");
            return;
        }

        string email = SessionManager.Instance.Email;

        await AuthService.Instance.DeleteAccount(email, password);
    }

    private void OnCancelClicked()
    {
        ClearFeedback();
        ClearInputs();
        ScreenNavigator.Instance.NavigateTo(ScreenName.MainApp);
    }

    private void OnPasswordToggleClicked()
    {
        _passwordVisible = !_passwordVisible;

        if (_passwordInput != null)
            _passwordInput.isPasswordField = !_passwordVisible;

        if (_passwordToggle != null)
            _passwordToggle.text = _passwordVisible ? "\U0001F440" : "\U0001F441";
    }

    private void OnContactSupportClicked()
    {
        Application.OpenURL(
            "mailto:support@ibmros.com?subject=Account%20Deactivation%20Request");
    }

    // AUTHSERVICE EVENT HANDLERS

    private void OnDeleteAccountSuccess(string message)
    {
        Debug.Log("[DeleteAccountController] Account deleted. Navigating to Login.");
        ScreenNavigator.Instance.NavigateTo(ScreenName.Login);
    }

    private void OnDeleteAccountFailed(string message, AuthError error)
    {
        ShowError(message);

        if (error == AuthError.WrongEmailOrPassword)
        {
            if (_passwordInput != null)
                _passwordInput.value = string.Empty;

            _passwordInput?.Focus();
        }
    }

    private void OnLoadingStateChanged(bool isLoading, string message)
    {
        SetLoadingState(isLoading);

        if (_loadingText != null)
            _loadingText.text = message;
    }

    // IAUTHUI IMPLEMENTATION

    public void ShowError(string message)
    {
        _feedbackCts?.Cancel();
        _feedbackCts = new CancellationTokenSource();
        _ = FeedbackHelper.ShowError(
            _feedbackBanner, _feedbackText, message, _feedbackCts.Token);
    }

    public void ShowSuccess(string message)
    {
        _feedbackCts?.Cancel();
        _feedbackCts = new CancellationTokenSource();
        _ = FeedbackHelper.ShowSuccess(
            _feedbackBanner, _feedbackText, message, _feedbackCts.Token);
    }

    public void ClearFeedback()
    {
        _feedbackCts?.Cancel();
        _feedbackCts = null;
        FeedbackHelper.Clear(_feedbackBanner, _feedbackText);

        if (_passwordError != null)
        {
            _passwordError.text = string.Empty;
            _passwordError.RemoveFromClassList("input-error-text--visible");
        }

        _passwordInputRow?.RemoveFromClassList("input-password-row--error");
    }

    public void SetLoadingState(bool isLoading)
    {
        if (isLoading)
            _loadingOverlay?.AddToClassList("loading-overlay--visible");
        else
            _loadingOverlay?.RemoveFromClassList("loading-overlay--visible");

        _confirmDeleteButton?.SetEnabled(!isLoading && _checkboxChecked);
        _cancelButton?.SetEnabled(!isLoading);
        _backToMainButton?.SetEnabled(!isLoading);
        _passwordInput?.SetEnabled(!isLoading);
        _confirmCheckbox?.SetEnabled(!isLoading);
    }

    // HELPERS

    private void ShowPasswordError(string message)
    {
        if (_passwordError == null)
            return;

        _passwordError.text = message;
        _passwordError.AddToClassList("input-error-text--visible");
        _passwordInputRow?.AddToClassList("input-password-row--error");
    }

    private void ClearInputs()
    {
        if (_passwordInput != null)
            _passwordInput.value = string.Empty;

        _checkboxChecked = false;

        if (_confirmCheckbox != null)
            _confirmCheckbox.RemoveFromClassList("delete-account-checkbox--checked");

        if (_checkboxIcon != null)
            _checkboxIcon.text = string.Empty;

        _passwordVisible = false;

        if (_passwordInput != null)
            _passwordInput.isPasswordField = true;

        if (_passwordToggle != null)
            _passwordToggle.text = "\U0001F441";

        UpdateDeleteButtonState();
    }
}