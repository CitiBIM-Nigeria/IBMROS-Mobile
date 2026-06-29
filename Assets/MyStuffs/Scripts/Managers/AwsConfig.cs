public static class AwsConfig
{
    // AWS Region where all your services are hosted
    public const string Region = "us-east-1";
    

    // Cognito Identity Pool - issues temporary credentials to all users
    public const string IdentityPoolId = "us-east-1:a1b45243-b537-4a69-af72-9764a2aeb9f2";

    // Cognito User Pool - handles signup, login, and password management
    public const string UserPoolId = "us-east-1_rn1iLPn7Y";
    public const string AppClientId = "7e21141nd2m90d9p459i0r6bul";

    // Cognito provider name - used when upgrading guest to authenticated credentials
    // Do not change this format, AWS requires it exactly like this
    public static string CognitoProviderName =>
        $"cognito-idp.{Region}.amazonaws.com/{UserPoolId}";

    // S3 bucket name for storing 3D furniture models.
    // MIGRATED to the new pipeline backend (ros-furniture-assets / ros-products).
    // Old values kept here for one-line rollback to the legacy DB:
    //   FurnitureBucketName        = "ibm-ros-furniture-models"
    //   FurnitureCatalogTableName  = "ibm-ros-furniture-catalog"
    //   FurnitureCatalogMerchantId = "0001"
    public const string FurnitureBucketName = "ros-furniture-assets";

    // Product catalog table written by ikea_pipeline.py
    public const string FurnitureCatalogTableName    = "ros-products";
    // Category tree: departments / categories / breadcrumb subcategories
    public const string CategoriesTableName          = "ros-categories";
    // Merchant partition the pipeline writes everything under
    public const string FurnitureCatalogMerchantId   = "ikea";


    /*// S3 bucket name for storing user room screenshots
    public const string UserContentBucketName = "ibm-ros-user-content";

    // DynamoDB table names
    public const string ProductsTableName = "ibm-ros-products";
    public const string UserLayoutsTableName = "ibm-ros-user-layouts";*/

    // Token expiry buffer in minutes
    // Refresh token before it expires by this many minutes
    public const int TokenRefreshBufferMinutes = 5;
    
    public const string CloudFrontDomain = "https://d3lz5hvxtvmgbq.cloudfront.net";
}