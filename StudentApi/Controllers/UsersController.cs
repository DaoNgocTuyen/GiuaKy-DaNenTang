using FirebaseAdmin.Auth;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using StudentApi.Models;

namespace StudentApi.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Policy = "AdminOnly")]
public class UsersController : ControllerBase
{
    private static readonly PasswordHasher<UserProfile> PasswordHasher = new();
    private readonly FirestoreDb _db;
    private readonly FirebaseAuth _auth;
    private readonly IConfiguration _configuration;
    private readonly ILogger<UsersController> _logger;

    public UsersController(
        FirestoreDb db,
        FirebaseAuth auth,
        IConfiguration configuration,
        ILogger<UsersController> logger)
    {
        _db = db;
        _auth = auth;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet("check-admin")]
    public IActionResult CheckAdmin()
    {
        return Ok(new { message = "Bạn có quyền admin." });
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        // Trả ảnh để web có thể hiển thị và sửa hồ sơ hiện tại.
        var snapshot = await _db.Collection("users")
            .GetSnapshotAsync();

        var users = snapshot.Documents
            .Select(document => document.ConvertTo<UserProfile>())
            .OrderBy(user => user.HoTen)
            .ToList();

        return Ok(users);
    }

    [HttpPost]
    public async Task<IActionResult> Create(UserInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Password))
        {
            return BadRequest(new
            {
                message = "Phải nhập mật khẩu khi tạo người dùng."
            });
        }

        if (!ImageData.TryDecode(input.HinhAnh, out _))
        {
            return BadRequest(new { message = "Ảnh phải là JPEG/PNG và không quá 400 KiB." });
        }

        var account = await _auth.CreateUserAsync(new UserRecordArgs
        {
            Email = input.Email.Trim(),
            Password = input.Password,
            DisplayName = input.HoTen.Trim()
        });

        var profile = new UserProfile
        {
            Email = account.Email,
            HoTen = input.HoTen.Trim(),
            HinhAnh = input.HinhAnh
        };
        profile.MatKhau = PasswordHasher.HashPassword(profile, input.Password);

        try
        {
            await _db.Collection("users")
                .Document(account.Uid)
                .SetAsync(profile);
        }
        catch
        {
            // Nếu ghi hồ sơ thất bại, cố gắng thu hồi tài khoản vừa tạo.
            try
            {
                await _auth.DeleteUserAsync(account.Uid);
            }
            catch (Exception rollbackError)
            {
                _logger.LogError(
                    rollbackError,
                    "Không thu hồi được tài khoản {Uid}.",
                    account.Uid);
            }

            throw;
        }

        profile.Uid = account.Uid;

        return StatusCode(StatusCodes.Status201Created, profile);
    }

    [HttpPut("{uid}")]
    public async Task<IActionResult> Update(string uid, UserInput input)
    {
        if (!ImageData.TryDecode(input.HinhAnh, out _))
        {
            return BadRequest(new { message = "Ảnh phải là JPEG/PNG và không quá 400 KiB." });
        }

        var reference = _db.Collection("users").Document(uid);
        var snapshot = await reference.GetSnapshotAsync();

        if (!snapshot.Exists)
        {
            return NotFound(new { message = "Không tìm thấy người dùng." });
        }

        var args = new UserRecordArgs
        {
            Uid = uid,
            Email = input.Email.Trim(),
            DisplayName = input.HoTen.Trim()
        };

        if (!string.IsNullOrWhiteSpace(input.Password))
        {
            args.Password = input.Password;
        }

        var profile = snapshot.ConvertTo<UserProfile>();
        var newPasswordHash = string.IsNullOrWhiteSpace(input.Password)
            ? null
            : PasswordHasher.HashPassword(profile, input.Password);
        var account = await _auth.UpdateUserAsync(args);

        await _db.RunTransactionAsync(async transaction =>
        {
            var current = await transaction.GetSnapshotAsync(reference);
            if (!current.Exists)
                throw new InvalidOperationException("Hồ sơ đã bị xóa trong khi cập nhật.");
            var updated = current.ConvertTo<UserProfile>();
            updated.Email = account.Email;
            updated.HoTen = input.HoTen.Trim();
            updated.HinhAnh = input.HinhAnh;
            if (newPasswordHash is not null)
                updated.MatKhau = newPasswordHash;
            // Ghi thay thế để mỗi hồ sơ luôn chỉ có đúng bốn trường.
            transaction.Set(reference, updated);
        });

        return Ok(new { message = "Đã cập nhật người dùng." });
    }

    [HttpDelete("{uid}")]
    public async Task<IActionResult> Delete(string uid)
    {
        var adminUids = _configuration
            .GetSection("Firebase:AdminUids")
            .Get<string[]>() ?? [];

        if (adminUids.Contains(uid))
        {
            return BadRequest(new
            {
                message = "Không xóa tài khoản admin trong bài thực hành."
            });
        }

        // Cho phép thử xóa lại nếu tài khoản Authentication đã bị xóa.
        try
        {
            await _auth.DeleteUserAsync(uid);
        }
        catch (FirebaseAuthException exception)
            when (exception.AuthErrorCode == AuthErrorCode.UserNotFound)
        {
        }

        await _db.Collection("users").Document(uid).DeleteAsync();

        return Ok(new { message = "Đã xóa người dùng." });
    }

}
