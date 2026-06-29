using System;
using System.Threading.Tasks;
using UnityEngine;

public class AuthService : MonoBehaviour
{
    public static AuthService Instance { get; private set; }

    // ============================================
    // EVENTS
    // Both UI Toolkit and UGUI controllers
    // subscribe to these instead of calling
    // AuthManager directly
    // ============================================

    // Login events
    public static event Action<string> OnLoginSuccess;
    public static event Action<string, AuthError> OnLoginFailed;

    // Sign up events
    public static event Action<string> OnSignUpSuccess;
    public static event Action<string, AuthError> OnSignUpFailed;

    // Confirm email events
    public static event Action<string> OnConfirmEmailSuccess;
    public static event Action<string, AuthError> OnConfirmEmailFailed;

    // Resend code events
    public static event Action<string> OnResendCodeSuccess;
    public static event Action<string, AuthError> OnResendCodeFailed;

    // Forgot password events
    public static event Action<string> OnForgotPasswordSuccess;
    public static event Action<string, AuthError> OnForgotPasswordFailed;

    // Reset password events
    public static event Action<string> OnResetPasswordSuccess;
    public static event Action<string, AuthError> OnResetPasswordFailed;

    // Logout events
    public static event Action<string> OnLogoutSuccess;
    public static event Action<string, AuthError> OnLogoutFailed;

    // Delete account events
    public static event Action<string> OnDeleteAccountSuccess;
    public static event Action<string, AuthError> OnDeleteAccountFailed;

    // Session events
    public static event Action OnSessionRestored;
    public static event Action OnSessionExpiredOrInvalid;

    // Loading event
    // bool = isLoading, string = message to display
    public static event Action<bool, string> OnLoadingChanged;

    // Network events forwarded from NetworkMonitor
    public static event Action OnNetworkLost;
    public static event Action OnNetworkRestored;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        // Forward network events so controllers
        // only need to subscribe to AuthService
        NetworkMonitor.OnConnectionLost += () => OnNetworkLost?.Invoke();
        NetworkMonitor.OnConnectionRestored += OnNetworkConnectionRestored;
        SessionRefreshService.OnSessionExpired += OnRefreshServiceSessionExpired;
    }

    void OnDestroy()
    {
        NetworkMonitor.OnConnectionLost -= () => OnNetworkLost?.Invoke();
        NetworkMonitor.OnConnectionRestored -= OnNetworkConnectionRestored;
        SessionRefreshService.OnSessionExpired -= OnRefreshServiceSessionExpired;
    }

    // ============================================
    // SESSION CHECK
    // Called on app start by SplashController
    // ============================================

    public async Task CheckSession()
    {
        if (AwsManager.Instance == null || !AwsManager.Instance.IsInitialized)
        {
            OnSessionExpiredOrInvalid?.Invoke();
            return;
        }

        if (!SessionManager.Instance.IsLoggedIn)
        {
            OnSessionExpiredOrInvalid?.Invoke();
            return;
        }

        if (SessionManager.Instance.IsTokenExpired())
        {
            OnLoadingChanged?.Invoke(true, "Refreshing session...");

            AuthResult result = await AuthManager.Instance.RefreshSession();

            OnLoadingChanged?.Invoke(false, string.Empty);

            if (result.IsSuccess)
            {
                Debug.Log("[AuthService] Session restored successfully.");
                OnSessionRestored?.Invoke();
            }
            else
            {
                Debug.Log("[AuthService] Session refresh failed.");
                OnSessionExpiredOrInvalid?.Invoke();
            }
        }
        else
        {
            AwsManager.Instance.UpgradeToAuthenticated(
                SessionManager.Instance.IdToken);

            Debug.Log("[AuthService] Session restored from cache.");
            OnSessionRestored?.Invoke();
        }
    }

    // ============================================
    // LOGIN
    // ============================================

    public async Task Login(string email, string password)
    {
        string validationError = InputValidator.ValidateLogin(email, password);
        if (validationError != null)
        {
            OnLoginFailed?.Invoke(validationError, AuthError.InvalidInput);
            return;
        }

        OnLoadingChanged?.Invoke(true, "Signing in...");

        AuthResult result = await AuthManager.Instance.Login(email, password);

        OnLoadingChanged?.Invoke(false, string.Empty);

        if (result.IsSuccess)
        {
            // Save last used email for pre-fill on next visit
            PlayerPrefs.SetString("ibm_ros_last_email", email);
            PlayerPrefs.Save();

            Debug.Log($"[AuthService] Login successful for {email}.");
            OnLoginSuccess?.Invoke(result.Message);
        }
        else
        {
            // Store pending email for confirm email screen
            if (result.Error == AuthError.EmailNotConfirmed)
            {
                PlayerPrefs.SetString("ibm_ros_pending_email", email);
                PlayerPrefs.Save();
            }

            Debug.Log($"[AuthService] Login failed: {result.Error}");
            OnLoginFailed?.Invoke(result.Message, result.Error ?? AuthError.UnknownError);
        }
    }

    // ============================================
    // SIGN UP
    // ============================================

    // Signup password held in MEMORY ONLY (never persisted) so the user can be
    // signed straight in after confirming their email. Cleared immediately after.
    private string _signupPassword;

    public async Task SignUp(string email, string password, string confirmPassword)
    {
        string validationError = InputValidator.ValidateSignUp(
            email, password, confirmPassword);

        if (validationError != null)
        {
            OnSignUpFailed?.Invoke(validationError, AuthError.InvalidInput);
            return;
        }

        OnLoadingChanged?.Invoke(true, "Creating account...");

        AuthResult result = await AuthManager.Instance.SignUp(email, password);

        OnLoadingChanged?.Invoke(false, string.Empty);

        if (result.IsSuccess)
        {
            PlayerPrefs.SetString("ibm_ros_pending_email", email);
            PlayerPrefs.Save();
            _signupPassword = password;   // for auto-login after email confirm

            Debug.Log($"[AuthService] Sign up successful for {email}.");
            OnSignUpSuccess?.Invoke(result.Message);
        }
        else
        {
            Debug.Log($"[AuthService] Sign up failed: {result.Error}");
            OnSignUpFailed?.Invoke(result.Message, result.Error ?? AuthError.UnknownError);
        }
    }

    // Returns the in-memory signup password (for post-confirm auto-login) and
    // clears it. Empty if unavailable (e.g. app restarted between sign-up steps).
    public string ConsumeSignupPassword()
    {
        string pw = _signupPassword;
        _signupPassword = null;
        return pw ?? string.Empty;
    }

    // ============================================
    // CONFIRM EMAIL
    // ============================================

    public async Task ConfirmEmail(string email, string code)
    {
        string validationError = InputValidator.ValidateConfirmationCode(code);
        if (validationError != null)
        {
            OnConfirmEmailFailed?.Invoke(validationError, AuthError.InvalidInput);
            return;
        }

        OnLoadingChanged?.Invoke(true, "Verifying code...");

        AuthResult result = await AuthManager.Instance.ConfirmEmail(email, code);

        OnLoadingChanged?.Invoke(false, string.Empty);

        if (result.IsSuccess)
        {
            PlayerPrefs.DeleteKey("ibm_ros_pending_email");
            PlayerPrefs.Save();

            Debug.Log($"[AuthService] Email confirmed for {email}.");
            OnConfirmEmailSuccess?.Invoke(result.Message);
        }
        else
        {
            Debug.Log($"[AuthService] Confirm email failed: {result.Error}");
            OnConfirmEmailFailed?.Invoke(
                result.Message, result.Error ?? AuthError.UnknownError);
        }
    }

    // ============================================
    // RESEND CONFIRMATION CODE
    // ============================================

    public async Task ResendConfirmationCode(string email)
    {
        OnLoadingChanged?.Invoke(true, "Sending verification code...");

        AuthResult result = await AuthManager.Instance.ResendConfirmationCode(email);

        OnLoadingChanged?.Invoke(false, string.Empty);

        if (result.IsSuccess)
        {
            Debug.Log($"[AuthService] Confirmation code resent to {email}.");
            OnResendCodeSuccess?.Invoke(result.Message);
        }
        else
        {
            Debug.Log($"[AuthService] Resend code failed: {result.Error}");
            OnResendCodeFailed?.Invoke(
                result.Message, result.Error ?? AuthError.UnknownError);
        }
    }

    // ============================================
    // FORGOT PASSWORD
    // ============================================

    public async Task ForgotPassword(string email)
    {
        string validationError = InputValidator.ValidateEmail(email);
        if (validationError != null)
        {
            OnForgotPasswordFailed?.Invoke(validationError, AuthError.InvalidInput);
            return;
        }

        OnLoadingChanged?.Invoke(true, "Sending reset code...");

        AuthResult result = await AuthManager.Instance.ForgotPassword(email);

        OnLoadingChanged?.Invoke(false, string.Empty);

        if (result.IsSuccess)
        {
            PlayerPrefs.SetString("ibm_ros_pending_email", email);
            PlayerPrefs.Save();

            Debug.Log($"[AuthService] Password reset code sent to {email}.");
            OnForgotPasswordSuccess?.Invoke(result.Message);
        }
        else
        {
            Debug.Log($"[AuthService] Forgot password failed: {result.Error}");
            OnForgotPasswordFailed?.Invoke(
                result.Message, result.Error ?? AuthError.UnknownError);
        }
    }

    // ============================================
    // RESET PASSWORD
    // ============================================

    public async Task ResetPassword(
        string email,
        string code,
        string newPassword,
        string confirmNewPassword)
    {
        string validationError = InputValidator.ValidateConfirmNewPassword(
            code, newPassword, confirmNewPassword);

        if (validationError != null)
        {
            OnResetPasswordFailed?.Invoke(validationError, AuthError.InvalidInput);
            return;
        }

        OnLoadingChanged?.Invoke(true, "Resetting password...");

        AuthResult result = await AuthManager.Instance.ConfirmNewPassword(
            email, code, newPassword);

        OnLoadingChanged?.Invoke(false, string.Empty);

        if (result.IsSuccess)
        {
            PlayerPrefs.DeleteKey("ibm_ros_pending_email");
            PlayerPrefs.Save();

            Debug.Log($"[AuthService] Password reset successful for {email}.");
            OnResetPasswordSuccess?.Invoke(result.Message);
        }
        else
        {
            Debug.Log($"[AuthService] Reset password failed: {result.Error}");
            OnResetPasswordFailed?.Invoke(
                result.Message, result.Error ?? AuthError.UnknownError);
        }
    }

    // ============================================
    // LOGOUT
    // ============================================

    public async Task Logout()
    {
        OnLoadingChanged?.Invoke(true, "Signing out...");

        AuthResult result = await AuthManager.Instance.Logout();

        OnLoadingChanged?.Invoke(false, string.Empty);

        if (SessionRefreshService.Instance != null)
            SessionRefreshService.Instance.Stop();

        if (result.IsSuccess)
        {
            Debug.Log("[AuthService] Logout successful.");
            OnLogoutSuccess?.Invoke(result.Message);
        }
        else
        {
            // Even on failure clear locally and treat as success
            // User should never be stuck on main app screen
            Debug.Log("[AuthService] Logout completed with error. Clearing session anyway.");
            OnLogoutSuccess?.Invoke("Logged out successfully.");
        }
    }

    // ============================================
    // DELETE ACCOUNT
    // ============================================

    public async Task DeleteAccount(string email, string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            OnDeleteAccountFailed?.Invoke(
                "Password is required to delete your account.",
                AuthError.InvalidInput);
            return;
        }

        if (string.IsNullOrEmpty(email))
        {
            OnDeleteAccountFailed?.Invoke(
                "Session expired. Please log in and try again.",
                AuthError.NoActiveSession);
            return;
        }

        OnLoadingChanged?.Invoke(true, "Deleting account...");

        AuthResult result = await AuthManager.Instance.DeleteAccount(email, password);

        OnLoadingChanged?.Invoke(false, string.Empty);

        if (result.IsSuccess)
        {
            if (SessionRefreshService.Instance != null)
                SessionRefreshService.Instance.Stop();

            Debug.Log("[AuthService] Account deleted successfully.");
            OnDeleteAccountSuccess?.Invoke(result.Message);
        }
        else
        {
            Debug.Log($"[AuthService] Delete account failed: {result.Error}");
            OnDeleteAccountFailed?.Invoke(
                result.Message, result.Error ?? AuthError.UnknownError);
        }
    }

    // ============================================
    // HELPER METHODS
    // ============================================

    // Returns the last used email for pre-filling login screen
    public string GetLastEmail()
    {
        return PlayerPrefs.GetString("ibm_ros_last_email", string.Empty);
    }

    // Returns the pending email for confirm email
    // and reset password screens
    public string GetPendingEmail()
    {
        return PlayerPrefs.GetString("ibm_ros_pending_email", string.Empty);
    }

    // Returns masked version of email for display
    public string GetMaskedEmail(string email)
    {
        if (string.IsNullOrEmpty(email) || !email.Contains("@"))
            return email;

        string[] parts = email.Split('@');
        string localPart = parts[0];
        string domain = parts[1];

        if (localPart.Length <= 2)
            return email;

        string visible = localPart.Substring(0, 2);
        string masked = new string('*', localPart.Length - 2);
        return $"{visible}{masked}@{domain}";
    }

    // ============================================
    // INTERNAL EVENT HANDLERS
    // ============================================

    private async void OnNetworkConnectionRestored()
    {
        OnNetworkRestored?.Invoke();

        // If token expired while offline refresh it now
        if (SessionManager.Instance != null &&
            SessionManager.Instance.IsLoggedIn &&
            SessionManager.Instance.IsTokenExpired())
        {
            OnLoadingChanged?.Invoke(true, "Reconnecting...");
            AuthResult result = await AuthManager.Instance.RefreshSession();
            OnLoadingChanged?.Invoke(false, string.Empty);

            if (!result.IsSuccess)
                OnSessionExpiredOrInvalid?.Invoke();
        }
    }

    private void OnRefreshServiceSessionExpired()
    {
        Debug.Log("[AuthService] Session expired from refresh service.");
        OnSessionExpiredOrInvalid?.Invoke();
    }
}