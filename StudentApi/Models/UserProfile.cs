using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Google.Cloud.Firestore;

namespace StudentApi.Models;

[FirestoreData]
public class UserProfile
{
    [FirestoreDocumentId]
    public string Uid { get; set; } = "";

    [FirestoreProperty("email")]
    public string Email { get; set; } = "";

    [FirestoreProperty("hoten")]
    [JsonPropertyName("hoten")]
    public string HoTen { get; set; } = "";

    [FirestoreProperty("matkhau")]
    [JsonIgnore]
    public string? MatKhau { get; set; }

    [FirestoreProperty("hinhanh")]
    [JsonPropertyName("hinhanh")]
    public string HinhAnh { get; set; } = "";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class UserInput
{
    [Required]
    [EmailAddress]
    [StringLength(254)]
    public string Email { get; set; } = "";

    [Required]
    [StringLength(100, MinimumLength = 2)]
    [JsonPropertyName("hoten")]
    public string HoTen { get; set; } = "";

    [Required, MaxLength(550000)]
    [JsonPropertyName("hinhanh")]
    public string HinhAnh { get; set; } = "";

    // Bắt buộc khi tạo, để null khi sửa mà không đổi mật khẩu.
    [StringLength(128, MinimumLength = 6)]
    [JsonPropertyName("matkhau")]
    public string? Password { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class AvatarInput
{
    [Required, MaxLength(550000)]
    [JsonPropertyName("hinhanh")]
    public string HinhAnh { get; set; } = "";
}
