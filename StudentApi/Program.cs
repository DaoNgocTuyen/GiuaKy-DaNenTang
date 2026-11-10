using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

var projectId = builder.Configuration["Firebase:ProjectId"]
    ?? throw new InvalidOperationException("Thiếu Firebase:ProjectId.");

var databaseId =
    builder.Configuration["Firebase:DatabaseId"] ?? "(default)";

var credentialsPath =
    builder.Configuration["Firebase:CredentialsPath"];

GoogleCredential credential;

if (!string.IsNullOrWhiteSpace(credentialsPath))
{
    var fullPath = Path.GetFullPath(
        credentialsPath,
        builder.Environment.ContentRootPath);

    using var stream = File.OpenRead(fullPath);

    credential = GoogleCredential.FromServiceAccountCredential(
        ServiceAccountCredential.FromServiceAccountData(stream));
}
else
{
    credential = GoogleCredential.GetApplicationDefault();
}

var firebaseApp = FirebaseApp.Create(new AppOptions
{
    Credential = credential,
    ProjectId = projectId
});

builder.Services.AddSingleton(FirebaseAuth.GetAuth(firebaseApp));

builder.Services.AddSingleton(new FirestoreDbBuilder
{
    ProjectId = projectId,
    DatabaseId = databaseId,
    GoogleCredential = credential
}.Build());

var adminUids = builder.Configuration
    .GetSection("Firebase:AdminUids")
    .Get<string[]>() ?? [];

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority =
            $"https://securetoken.google.com/{projectId}";

        options.Audience = projectId;
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = $"https://securetoken.google.com/{projectId}",

            ValidateAudience = true,
            ValidAudience = projectId,

            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            NameClaimType = "sub",
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy =>
    {
        policy.RequireAuthenticatedUser();

        policy.RequireAssertion(context =>
        {
            var uid = context.User.FindFirst("sub")?.Value;

            return uid is not null && adminUids.Contains(uid);
        });
    });
});

var app = builder.Build();

// Trả lỗi JSON để giao diện xử lý.
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (FirebaseAuthException exception)
    {
        app.Logger.LogWarning(
            "Firebase Authentication error: {Code}",
            exception.AuthErrorCode);

        var code = exception.AuthErrorCode?.ToString() ?? "AuthError";

        context.Response.StatusCode = code switch
        {
            "EmailAlreadyExists" => 409,
            "UserNotFound" => 404,
            _ => 400
        };

        await context.Response.WriteAsJsonAsync(new
        {
            message = $"Firebase Authentication: {code}"
        });
    }
    catch (Exception exception)
    {
        app.Logger.LogError(exception, "Request failed.");

        context.Response.StatusCode = 500;

        await context.Response.WriteAsJsonAsync(new
        {
            message = "Xử lý thất bại. Kiểm tra terminal của StudentApi."
        });
    }
});

// Trong development cho phép HTTP để Android Emulator gọi API.
// Khi triển khai thật phải dùng HTTPS.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

if (app.Environment.IsDevelopment())
{
    app.MapGet("/firebase-test", async (FirestoreDb db) =>
    {
        using var timeout =
            new CancellationTokenSource(TimeSpan.FromSeconds(15));

        await db.Collection("_connection_checks")
            .Document("health")
            .GetSnapshotAsync(cancellationToken: timeout.Token);

        return Results.Ok(new
        {
            connected = true,
            projectId = db.ProjectId,
            databaseId = db.DatabaseId
        });
    });
}

app.Run();