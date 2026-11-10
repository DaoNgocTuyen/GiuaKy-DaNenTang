using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StudentApi.Models;

namespace StudentApi.Controllers;

[ApiController]
[Route("api/me")]
[Authorize]
public class ProfileController : ControllerBase
{
    private readonly FirestoreDb _db;

    public ProfileController(FirestoreDb db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetMe()
    {
        var uid = User.FindFirst("sub")!.Value;
        var snapshot = await _db.Collection("users").Document(uid).GetSnapshotAsync();
        if (!snapshot.Exists)
            return NotFound(new { message = "Chưa có hồ sơ. Hãy dùng tài khoản do web admin tạo." });
        return Ok(snapshot.ConvertTo<UserProfile>());
    }

    [HttpPut("avatar")]
    [RequestSizeLimit(1_000_000)]
    public async Task<IActionResult> UpdateAvatar(AvatarInput input)
    {
        if (!ImageData.TryDecode(input.HinhAnh, out _))
            return BadRequest(new { message = "Ảnh phải là JPEG/PNG và không quá 400 KiB." });

        var uid = User.FindFirst("sub")!.Value;
        var reference = _db.Collection("users").Document(uid);
        var exists = await _db.RunTransactionAsync(async transaction =>
        {
            var snapshot = await transaction.GetSnapshotAsync(reference);
            if (!snapshot.Exists) return false;
            var profile = snapshot.ConvertTo<UserProfile>();
            profile.HinhAnh = input.HinhAnh;
            transaction.Set(reference, profile);
            return true;
        });
        if (!exists)
            return NotFound(new { message = "Chưa có hồ sơ người dùng." });
        return Ok(new { message = "Đã cập nhật ảnh đại diện." });
    }
}