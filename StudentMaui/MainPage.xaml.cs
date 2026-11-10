using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Media;
using Microsoft.Maui.Storage;

namespace StudentMaui;

public partial class MainPage : ContentPage
{
    private const string FirebaseApiKey = "AIzaSyC2CxzvTaSvbaykOAwJb2UEBI4XgRDZXBg";

#if ANDROID
    private const string ApiBaseUrl = "http://10.0.2.2:5258/";
#else
    private const string ApiBaseUrl = "https://localhost:7030/";
#endif

    private readonly HttpClient _api = new()
    {
        BaseAddress = new Uri(ApiBaseUrl),
        Timeout = TimeSpan.FromSeconds(30)
    };

    private bool _busy;

    public MainPage()
    {
        InitializeComponent();
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (_busy) return;

        _busy = true;
        StatusLabel.Text = "";

        try
        {
            await action();
        }
        catch (Exception exception)
        {
            StatusLabel.Text = exception.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    private static async Task<JsonElement> ReadJsonAsync(
        HttpResponseMessage response)
    {
        using (response)
        {
            var text = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                var message = response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized =>
                        "Phiên hết hạn hoặc không hợp lệ. Hãy đăng nhập lại.",

                    HttpStatusCode.Forbidden =>
                        "Bạn không có quyền thực hiện thao tác.",

                    _ => $"Lỗi HTTP {(int)response.StatusCode}"
                };

                try
                {
                    using var errorDocument = JsonDocument.Parse(text);
                    var root = errorDocument.RootElement;

                    if (root.TryGetProperty("message", out var apiMessage))
                    {
                        message = apiMessage.GetString() ?? message;
                    }
                    else if (
                        root.TryGetProperty("error", out var error)
                        && error.ValueKind == JsonValueKind.Object
                        && error.TryGetProperty(
                            "message",
                            out var firebaseMessage))
                    {
                        message = firebaseMessage.GetString() ?? message;
                    }
                }
                catch (JsonException)
                {
                    // Một số lỗi 401/403 không có JSON.
                }

                throw new Exception(message);
            }

            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
    }

    private async void OnLoginClicked(object sender, EventArgs e)
    {
        await RunAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(EmailEntry.Text)
                || string.IsNullOrEmpty(PasswordEntry.Text))
            {
                throw new Exception("Hãy nhập email và mật khẩu.");
            }

            using var firebaseClient = new HttpClient();

            var response = await firebaseClient.PostAsJsonAsync(
                "https://identitytoolkit.googleapis.com/v1/" +
                $"accounts:signInWithPassword?key={FirebaseApiKey}",
                new
                {
                    email = EmailEntry.Text.Trim(),
                    password = PasswordEntry.Text,
                    returnSecureToken = true
                });

            var result = await ReadJsonAsync(response);
            var token = result.GetProperty("idToken").GetString();

            _api.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            try
            {
                await LoadProfileAsync();
            }
            catch
            {
                _api.DefaultRequestHeaders.Authorization = null;
                throw;
            }

            PasswordEntry.Text = "";

            LoginPanel.IsVisible = false;
            ProfilePanel.IsVisible = true;
        });
    }

    private async Task LoadProfileAsync()
    {
        var response = await _api.GetAsync("api/me");
        var profile = await ReadJsonAsync(response);

        NameLabel.Text = profile.GetProperty("hoten").GetString();

        EmailLabel.Text =
            "Email: " + profile.GetProperty("email").GetString();

        var image = profile.GetProperty("hinhanh").GetString();

        if (string.IsNullOrEmpty(image))
        {
            AvatarImage.Source = null;
            return;
        }

        var commaIndex = image.IndexOf(',');

        if (commaIndex < 0)
            throw new Exception("Dữ liệu ảnh không hợp lệ.");

        var bytes = Convert.FromBase64String(image[(commaIndex + 1)..]);

        AvatarImage.Source = ImageSource.FromStream(
            () => new MemoryStream(bytes));
    }

    private static MediaPickerOptions PhotoOptions()
    {
        return new MediaPickerOptions
        {
            Title = "Ảnh đại diện",
            MaximumWidth = 400,
            MaximumHeight = 400,
            CompressionQuality = 70
        };
    }

    private async void OnPickPhotoClicked(object sender, EventArgs e)
    {
        await RunAsync(async () =>
        {
            var photo = await MediaPicker.Default.PickPhotoAsync(
                PhotoOptions());

            if (photo is not null)
                await UploadPhotoAsync(photo);
        });
    }

    private async void OnCapturePhotoClicked(object sender, EventArgs e)
    {
        await RunAsync(async () =>
        {
            if (!MediaPicker.Default.IsCaptureSupported)
                throw new Exception("Thiết bị không hỗ trợ chụp ảnh.");

            var permission =
                await Permissions.RequestAsync<Permissions.Camera>();

            if (permission != PermissionStatus.Granted)
                throw new Exception("Chưa được cấp quyền camera.");

            var photo = await MediaPicker.Default.CapturePhotoAsync(
                PhotoOptions());

            if (photo is not null)
                await UploadPhotoAsync(photo);
        });
    }

    private async Task UploadPhotoAsync(FileResult photo)
    {
        await using var stream = await photo.OpenReadAsync();
        using var memory = new MemoryStream();

        await stream.CopyToAsync(memory);

        if (memory.Length == 0 || memory.Length > 400 * 1024)
        {
            throw new Exception(
                "Ảnh phải nhỏ hơn hoặc bằng 400 KB.");
        }

        var bytes = memory.ToArray();

        var isPng = bytes.Length >= 8
            && bytes.AsSpan(0, 8).SequenceEqual(
                new byte[]
                {
                    137, 80, 78, 71, 13, 10, 26, 10
                });

        var mimeType = isPng ? "image/png" : "image/jpeg";

        var image =
            $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}";

        var response = await _api.PutAsJsonAsync(
            "api/me/avatar",
            new { hinhanh = image });

        await ReadJsonAsync(response);
        await LoadProfileAsync();

        StatusLabel.Text = "Đã cập nhật ảnh đại diện.";
    }

    private async void OnReloadClicked(object sender, EventArgs e)
    {
        await RunAsync(LoadProfileAsync);
    }

    private void OnLogoutClicked(object sender, EventArgs e)
    {
        if (_busy) return;

        _api.DefaultRequestHeaders.Authorization = null;

        EmailEntry.Text = "";
        PasswordEntry.Text = "";

        NameLabel.Text = "";
        EmailLabel.Text = "";
        AvatarImage.Source = null;

        ProfilePanel.IsVisible = false;
        LoginPanel.IsVisible = true;

        StatusLabel.Text = "Đã đăng xuất.";
    }
}